using System.Numerics;
using System.Runtime.InteropServices;
using Engine.Graphics;
using Hexa.NET.ImGui;
using Diligent;

namespace Engine.UI.ImGUI;

internal sealed unsafe class ImGuiRenderer : IDisposable
{
    private const string VertexSource = """
        cbuffer Constants { float4x4 ProjectionMatrix; }

        struct VSInput { float2 pos : ATTRIB0; float2 uv : ATTRIB1; float4 col : ATTRIB2; };
        struct PSInput { float4 pos : SV_POSITION; float4 col : COLOR; float2 uv : TEXCOORD; };

        void main(in VSInput vs, out PSInput ps)
        {
            ps.pos = mul(ProjectionMatrix, float4(vs.pos, 0.0, 1.0));
            ps.col = vs.col;
            ps.uv  = vs.uv;
        }
        """;

    private const string PixelSource = """
        struct PSInput { float4 pos : SV_POSITION; float4 col : COLOR; float2 uv : TEXCOORD; };

        Texture2D    Texture;
        SamplerState Texture_sampler;

        float4 main(in PSInput ps) : SV_Target
        {
            float4 c = ps.col * Texture.Sample(Texture_sampler, ps.uv);
        #ifdef SRGB_TARGET
            c.rgb = pow(abs(c.rgb), float3(2.2, 2.2, 2.2));
        #endif
            return c;
        }
        """;

    private readonly IShader _vs;
    private readonly IShader _ps;
    private readonly IPipelineState _pso;
    private readonly IShaderResourceBinding _srb;
    private readonly IBuffer _constants;
    private  ITexture _fontTexture = null!;
    private readonly GraphicsContext gfx;
    private byte[] _shadow = [];          // RGBA-копия атласа на CPU
    private int _texW, _texH;   

    private IBuffer? _vb, _ib;
    private int _vbCapacity, _ibCapacity;   // в элементах

    internal ImGuiRenderer(GraphicsContext gfx)
    {
        this.gfx = gfx;
        var device = gfx.Device;

        var io = ImGui.GetIO();
        io.BackendFlags |= ImGuiBackendFlags.RendererHasVtxOffset;   // рисуем с BaseVertex, лимита в 64к вершин нет

        var scDesc = gfx.SwapChain?.GetDesc();
        bool srgb = scDesc?.ColorBufferFormat is TextureFormat.BGRA8_UNorm_sRGB or TextureFormat.RGBA8_UNorm_sRGB;
        string prefix = srgb ? "#define SRGB_TARGET 1\n" : "";

        _vs = device!.CreateShader(new()
        {
            Source = VertexSource,
            Desc = new() { Name = "ImGui VS", ShaderType = ShaderType.Vertex, UseCombinedTextureSamplers = true },
            SourceLanguage = ShaderSourceLanguage.Hlsl
        }, out _);

        _ps = device!.CreateShader(new()
        {
            Source = prefix + PixelSource,
            Desc = new() { Name = "ImGui PS", ShaderType = ShaderType.Pixel, UseCombinedTextureSamplers = true },
            SourceLanguage = ShaderSourceLanguage.Hlsl
        }, out _);

        if (_vs == null || _ps == null)
            throw new Exception("ImGui shader creation failed.");

        _constants = device.CreateBuffer(new()
        {
            Name = "ImGui constants",
            Size = (ulong)sizeof(Matrix4x4),
            Usage = Usage.Dynamic,
            BindFlags = BindFlags.UniformBuffer,
            CPUAccessFlags = CpuAccessFlags.Write
        });

        _pso = device.CreateGraphicsPipelineState(new()
        {
            PSODesc = new()
            {
                Name = "ImGui PSO",
                ResourceLayout = new()
                {
                    DefaultVariableType = ShaderResourceVariableType.Static,
                    Variables = new[]
                    {
                        new ShaderResourceVariableDesc
                        {
                            ShaderStages = ShaderType.Pixel,
                            Name = "Texture",
                            Type = ShaderResourceVariableType.Mutable
                        }
                    },
                    ImmutableSamplers = new[]
                    {
                        new ImmutableSamplerDesc
                        {
                            ShaderStages = ShaderType.Pixel,
                            SamplerOrTextureName = "Texture",
                            Desc = new()
                            {
                                MinFilter = FilterType.Linear,
                                MagFilter = FilterType.Linear,
                                MipFilter = FilterType.Linear,
                                AddressU = TextureAddressMode.Clamp,
                                AddressV = TextureAddressMode.Clamp,
                                AddressW = TextureAddressMode.Clamp
                            }
                        }
                    }
                }
            },
            Vs = _vs,
            Ps = _ps,
            GraphicsPipeline = new()
            {
                InputLayout = new()
                {
                    LayoutElements = new[]
                    {
                        new LayoutElement { InputIndex = 0, NumComponents = 2, ValueType = Diligent.ValueType.Float32 },
                        new LayoutElement { InputIndex = 1, NumComponents = 2, ValueType = Diligent.ValueType.Float32 },
                        new LayoutElement { InputIndex = 2, NumComponents = 4, ValueType = Diligent.ValueType.UInt8, IsNormalized = true }
                    }
                },
                PrimitiveTopology = PrimitiveTopology.TriangleList,
                RasterizerDesc = new() { CullMode = CullMode.None, ScissorEnable = true },
                DepthStencilDesc = new() { DepthEnable = false },
                BlendDesc = new()
                {
                    RenderTargets =
                    [
                        new RenderTargetBlendDesc
                        {
                            BlendEnable = true,
                            SrcBlend = BlendFactor.SrcAlpha,
                            DestBlend = BlendFactor.InvSrcAlpha,
                            BlendOp = BlendOperation.Add,
                            SrcBlendAlpha = BlendFactor.One,
                            DestBlendAlpha = BlendFactor.InvSrcAlpha,
                            BlendOpAlpha = BlendOperation.Add,
                            RenderTargetWriteMask = ColorMask.All
                        }
                    ]
                },
                NumRenderTargets = 1,
                RTVFormats = [scDesc!.Value.ColorBufferFormat]
            }
        });

        if (_pso == null)
            throw new Exception("ImGui pipeline creation failed.");

        _pso.GetStaticVariableByName(ShaderType.Vertex, "Constants").Set(_constants, SetShaderResourceFlags.None);
        _srb = _pso.CreateShaderResourceBinding(true);
    }

    public void Render(ImDrawDataPtr drawData)
    {
        UpdateTextures(drawData);
        if (drawData.CmdListsCount == 0 || drawData.DisplaySize.X <= 0 || drawData.DisplaySize.Y <= 0)
            return;

        var ctx = gfx.Context;
        EnsureBuffers(drawData.TotalVtxCount, drawData.TotalIdxCount);

        var vtx = ctx!.MapBuffer<ImDrawVert>(_vb!, MapType.Write, MapFlags.Discard);
        var idx = ctx!.MapBuffer<ushort>(_ib!, MapType.Write, MapFlags.Discard);
        int v = 0, i = 0;
        for (int n = 0; n < drawData.CmdListsCount; n++)
        {
            ImDrawListPtr list = drawData.CmdLists[n];
            new ReadOnlySpan<ImDrawVert>(list.VtxBuffer.Data, list.VtxBuffer.Size).CopyTo(vtx.Slice(v));
            new ReadOnlySpan<ushort>(list.IdxBuffer.Data, list.IdxBuffer.Size).CopyTo(idx.Slice(i));
            v += list.VtxBuffer.Size;
            i += list.IdxBuffer.Size;
        }
        ctx.UnmapBuffer(_vb!, MapType.Write);
        ctx.UnmapBuffer(_ib!, MapType.Write);

        float l = drawData.DisplayPos.X, r = l + drawData.DisplaySize.X;
        float t = drawData.DisplayPos.Y, b = t + drawData.DisplaySize.Y;
        var proj = new Matrix4x4(
            2f / (r - l), 0, 0, 0,
            0, 2f / (t - b), 0, 0,
            0, 0, 0.5f, 0,
            (r + l) / (l - r), (t + b) / (b - t), 0.5f, 1);
        var cb = ctx.MapBuffer<Matrix4x4>(_constants, MapType.Write, MapFlags.Discard);
        cb[0] = proj;
        ctx.UnmapBuffer(_constants, MapType.Write);

        var rtv = gfx.SwapChain?.GetCurrentBackBufferRTV();
        ctx.SetRenderTargets(new[] { rtv }, null, ResourceStateTransitionMode.Transition);
        ctx.SetPipelineState(_pso);
        ctx.SetVertexBuffers(0, new[] { _vb! }, new[] { 0ul }, ResourceStateTransitionMode.Transition);
        ctx.SetIndexBuffer(_ib!, 0, ResourceStateTransitionMode.Transition);
        ctx.CommitShaderResources(_srb, ResourceStateTransitionMode.Transition);

        Vector2 scale = drawData.FramebufferScale;
        uint rtW = (uint)(drawData.DisplaySize.X * scale.X);
        uint rtH = (uint)(drawData.DisplaySize.Y * scale.Y);

        int globalVtx = 0, globalIdx = 0;
        for (int n = 0; n < drawData.CmdListsCount; n++)
        {
            ImDrawListPtr list = drawData.CmdLists[n];
            for (int c = 0; c < list.CmdBuffer.Size; c++)
            {
                ref ImDrawCmd cmd = ref list.CmdBuffer.Data[c];

                float x0 = (cmd.ClipRect.X - drawData.DisplayPos.X) * scale.X;
                float y0 = (cmd.ClipRect.Y - drawData.DisplayPos.Y) * scale.Y;
                float x1 = (cmd.ClipRect.Z - drawData.DisplayPos.X) * scale.X;
                float y1 = (cmd.ClipRect.W - drawData.DisplayPos.Y) * scale.Y;
                if (x1 <= x0 || y1 <= y0) continue;

                ctx.SetScissorRects(new[]
                {
                    new Rect { Left = (int)x0, Top = (int)y0, Right = (int)x1, Bottom = (int)y1 }
                }, rtW, rtH);

                ctx.DrawIndexed(new()
                {
                    IndexType = Diligent.ValueType.UInt16,
                    NumIndices = cmd.ElemCount,
                    FirstIndexLocation = (uint)(globalIdx + cmd.IdxOffset),
                    BaseVertex = (uint)(globalVtx + cmd.VtxOffset),
                    Flags = DrawFlags.VerifyAll
                });
            }
            globalVtx += list.VtxBuffer.Size;
            globalIdx += list.IdxBuffer.Size;
        }
    }

    private void EnsureBuffers(int vertexCount, int indexCount)
    {
        var device = gfx.Device;

        if (_vb == null || _vbCapacity < vertexCount)
        {
            _vb?.Dispose();
            _vbCapacity = vertexCount + 5000;
            _vb = device!.CreateBuffer(new()
            {
                Name = "ImGui vertex buffer",
                Size = (ulong)(_vbCapacity * sizeof(ImDrawVert)),
                Usage = Usage.Dynamic,
                BindFlags = BindFlags.VertexBuffer,
                CPUAccessFlags = CpuAccessFlags.Write
            });
        }

        if (_ib == null || _ibCapacity < indexCount)
        {
            _ib?.Dispose();
            _ibCapacity = indexCount + 10000;
            _ib = device!.CreateBuffer(new()
            {
                Name = "ImGui index buffer",
                Size = (ulong)(_ibCapacity * sizeof(ushort)),
                Usage = Usage.Dynamic,
                BindFlags = BindFlags.IndexBuffer,
                CPUAccessFlags = CpuAccessFlags.Write
            });
        }
    }

    private void UpdateTextures(ImDrawDataPtr drawData)
    {
        for (int i = 0; i < drawData.Textures.Size; i++)
        {
            ImTextureDataPtr tex = drawData.Textures.Data[i];
            switch (tex.Status)
            {
                case ImTextureStatus.WantCreate:
                    CreateAtlas(tex);
                    tex.SetTexID(1ul);
                    tex.SetStatus(ImTextureStatus.Ok);
                    break;

                case ImTextureStatus.WantUpdates:
                    if (tex.Width != _texW || tex.Height != _texH) CreateAtlas(tex);
                    else UpdateAtlasRect(tex);
                    tex.SetTexID(1ul);
                    tex.SetStatus(ImTextureStatus.Ok);
                    break;

                case ImTextureStatus.WantDestroy:
                    // текстуру не трогаем: атлас у нас один и пересоздаётся в CreateAtlas
                    tex.SetStatus(ImTextureStatus.Destroyed);
                    break;
            }
        }
    }

    private void ConvertRect(ImTextureDataPtr tex, int x, int y, int w, int h)
    {
        byte* src = (byte*)tex.GetPixels();
        bool alpha8 = tex.Format == ImTextureFormat.Alpha8;
        int bpp = alpha8 ? 1 : 4;
        for (int row = y; row < y + h; row++)
        for (int col = x; col < x + w; col++)
        {
            int p = row * _texW + col;
            int d = p * 4;
            if (alpha8) { _shadow[d] = _shadow[d + 1] = _shadow[d + 2] = 255; _shadow[d + 3] = src[p]; }
            else { _shadow[d] = src[p * 4]; _shadow[d + 1] = src[p * 4 + 1]; _shadow[d + 2] = src[p * 4 + 2]; _shadow[d + 3] = src[p * 4 + 3]; }
        }
    }

    private void CreateAtlas(ImTextureDataPtr tex)
    {
        _texW = tex.Width; _texH = tex.Height;
        _shadow = new byte[_texW * _texH * 4];
        ConvertRect(tex, 0, 0, _texW, _texH);

        ITexture texture;
        fixed (byte* p = _shadow)
        {
            texture = gfx.Device!.CreateTexture(new()
            {
                Name = "ImGui font atlas",
                Type = ResourceDimension.Tex2d,
                Width = (uint)_texW, Height = (uint)_texH,
                MipLevels = 1,
                Format = TextureFormat.RGBA8_UNorm,
                BindFlags = BindFlags.ShaderResource,
                Usage = Usage.Default
            }, new TextureData
            {
                SubResources = new[] { new TextureSubResData { Data = (nint)p, Stride = (ulong)(_texW * 4) } }
            });
        }

        _srb.GetVariableByName(ShaderType.Pixel, "Texture")
            .Set(texture.GetDefaultView(TextureViewType.ShaderResource), SetShaderResourceFlags.None);
        _fontTexture?.Dispose();
        _fontTexture = texture;
    }

    private void UpdateAtlasRect(ImTextureDataPtr tex)
    {
        var r = tex.UpdateRect;                       // X, Y, W, H
        int x = r.X, y = r.Y, w = r.W, h = r.H;
        if (w <= 0 || h <= 0) return;

        ConvertRect(tex, x, y, w, h);
        fixed (byte* p = _shadow)
        {
            gfx.Context!.UpdateTexture(
                _fontTexture, 0, 0,
                new Box { MinX = (uint)x, MaxX = (uint)(x + w), MinY = (uint)y, MaxY = (uint)(y + h) },
                new TextureSubResData { Data = (nint)(p + (y * _texW + x) * 4), Stride = (ulong)(_texW * 4) },
                ResourceStateTransitionMode.Transition, ResourceStateTransitionMode.Transition);
        }
    }

    public void Dispose()
    {
        _srb?.Dispose();
        _pso?.Dispose();
        _fontTexture?.Dispose();
        _constants?.Dispose();
        _vb?.Dispose();
        _ib?.Dispose();
        _ps?.Dispose();
        _vs?.Dispose();
    }
}
