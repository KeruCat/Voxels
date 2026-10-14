using System.Numerics;
using System.Runtime.InteropServices;
using Diligent;
using Engine;
using Engine.Graphics;
using Engine.Graphics.Images;
using Engine.Graphics.Memory;
using Engine.Graphics.Shaders;
using Engine.Graphics.Shaders.Generated;
using Game.Voxels;
using System.IO;

namespace Game.Render;

/// <summary>
/// Рисует все видимые чанки из двух больших арен (вершины/индексы) одним набором состояний.
/// origin каждого чанка идёт вторым вершинным потоком (per-instance), поэтому на чанк — один DrawIndexed без смены констант.
/// </summary>
/// <summary>Способы отрисовки чанков.</summary>
public enum DrawMode
{
    Direct,           // цикл DrawIndexed (проверенный путь)
    IndirectSingle,   // DrawIndexedIndirect по одному чанку (проверка DrawCount/stride)
    IndirectMulti,    // один DrawIndexedIndirect на все чанки
}

internal sealed unsafe class VoxelRenderer : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    private struct ChunkConstants
    {
        public Matrix4x4 ViewProjection;
        public Vector4 Fade;        // x=время, y=длительность появления
        public Vector4 AtlasInfo;   // x=колонок, y=строк, z=ширина атласа px, w=высота атласа px
    }
    private readonly GraphicsContext _gfx;
    private readonly GpuArena _vtx, _idx;
    private readonly IPipelineState _pso;
    private readonly IShaderResourceBinding _srb;
    private readonly IBuffer _constants;
    private readonly ITexture _atlas;      // атлас блоков (Blocks.png), сэмплится в пиксельном шейдере
    private IBuffer _origins = null!;
    private IBuffer _indirect = null!;     // аргументы DrawIndexedIndirect: тот же 20-байтовый DrawIndexedCmd

    /// <summary>Способ отрисовки. Нужен для поиска ошибки: сравни режимы на одной и той же сцене.</summary>
    public DrawMode Mode { get; set; } = DrawMode.IndirectMulti;
    private int _capacity;

    public VoxelRenderer(GraphicsContext gfx, GpuArena vtx, GpuArena idx, string atlasPath)
    {
        (_gfx, _vtx, _idx) = (gfx, vtx, idx);
        var device = gfx.Device!;
        _atlas = CreateAtlas(device, atlasPath);
        var sc = gfx.SwapChain!.GetDesc();

        IShader vs, ps;
        using (var lib = new ShaderLibrary(AppContext.BaseDirectory, gfx))
        {
            vs = lib.Create(ShaderCatalog.Voxel.Vertex);
            ps = lib.Create(ShaderCatalog.Voxel.Fragment);
        }

        _constants = device.CreateBuffer(new()
        {
            Name = "Chunk constants",
            Size = (ulong)sizeof(ChunkConstants),
            Usage = Usage.Dynamic,
            BindFlags = BindFlags.UniformBuffer,
            CPUAccessFlags = CpuAccessFlags.Write
        });

        _pso = device.CreateGraphicsPipelineState(new()
        {
            PSODesc = new()
            {
                Name = "Voxel PSO",
                ResourceLayout = new()
                {
                    DefaultVariableType = ShaderResourceVariableType.Static,
                    Variables = new[]
                    {
                        new ShaderResourceVariableDesc
                        {
                            ShaderStages = ShaderType.Pixel,
                            Name = "atlas_0",
                            Type = ShaderResourceVariableType.Mutable
                        }
                    },
                    ImmutableSamplers = new[]
                    {
                        new ImmutableSamplerDesc
                        {
                            ShaderStages = ShaderType.Pixel,
                            SamplerOrTextureName = "atlas_0",
                            // пиксель-арт: без сглаживания, тайлы не смешиваются с соседями
                            Desc = new()
                            {
                                MinFilter = FilterType.Point,
                                MagFilter = FilterType.Point,
                                MipFilter = FilterType.Point,
                                AddressU = TextureAddressMode.Clamp,
                                AddressV = TextureAddressMode.Clamp,
                                AddressW = TextureAddressMode.Clamp
                            }
                        }
                    }
                }
            },
            Vs = vs,
            Ps = ps,
            GraphicsPipeline = new()
            {
                InputLayout = new()
                {
                    LayoutElements = new[]
                    {
                        // слот 0: VoxelVertex (8 байт) = PosFace(4×u8) + Packed(u32: тайл, повтор, угол)
                        new LayoutElement { InputIndex = 0, BufferSlot = 0, NumComponents = 4, ValueType = Diligent.ValueType.UInt8, IsNormalized = false },
                        new LayoutElement { InputIndex = 1, BufferSlot = 0, NumComponents = 1, ValueType = Diligent.ValueType.UInt32, IsNormalized = false },
                        // слот 1: origin чанка (xyz) + время начала показа (w), один на инстанс
                        new LayoutElement { InputIndex = 2, BufferSlot = 1, NumComponents = 4, ValueType = Diligent.ValueType.Float32,
                                            Frequency = InputElementFrequency.PerInstance, InstanceDataStepRate = 1 }
                    }
                },
                PrimitiveTopology = PrimitiveTopology.TriangleList,
                // GreedyMesher: лицевая грань = против часовой. Если мир виден «наизнанку» — переключи FrontCounterClockwise.
                RasterizerDesc = new() { CullMode = CullMode.Back, FrontCounterClockwise = true },
                DepthStencilDesc = new() { DepthEnable = true, DepthWriteEnable = true, DepthFunc = ComparisonFunction.Less },
                NumRenderTargets = 1,
                RTVFormats = [sc.ColorBufferFormat],
                DSVFormat = sc.DepthBufferFormat
            }
        }) ?? throw new Exception("Voxel pipeline creation failed.");

        // имя cbuffer в скомпилированном HLSL (ShaderTool/Slang): cb_constants_0
        var cb = _pso.GetStaticVariableByName(ShaderType.Vertex, "cb_constants_0")
              ?? _pso.GetStaticVariableByName(ShaderType.Vertex, "constants")
              ?? throw new Exception("Voxel shader: constant buffer variable not found.");
        cb.Set(_constants, SetShaderResourceFlags.None);
        _srb = _pso.CreateShaderResourceBinding(true);
        // Slang добавляет суффикс _0 к именам ресурсов (как cb_constants_0); без совпадения имени переменная не находится
        var atlasVar = _srb.GetVariableByName(ShaderType.Pixel, "atlas_0")
                       ?? throw new Exception("Voxel shader: atlas texture variable 'atlas_0' not found.");
        atlasVar.Set(_atlas.GetDefaultView(TextureViewType.ShaderResource), SetShaderResourceFlags.None);

        GrowBuffers(1024);
    }

    /// <summary>Загружает Blocks.png (RGBA8) в текстуру. Размер должен совпасть с Atlas.Width × Atlas.Height.</summary>
    private static ITexture CreateAtlas(IRenderDevice device, string path)
    {
        var img = PngDecoder.Decode(File.ReadAllBytes(path));
        if (img.Width != Atlas.Width || img.Height != Atlas.Height)
            throw new InvalidDataException($"Atlas must be {Atlas.Width}x{Atlas.Height}, got {img.Width}x{img.Height}: {path}");

        fixed (byte* p = img.Rgba)
        {
            return device.CreateTexture(new()
            {
                Name = "Block atlas",
                Type = ResourceDimension.Tex2d,
                Width = (uint)img.Width, Height = (uint)img.Height,
                MipLevels = 1,
                Format = TextureFormat.RGBA8_UNorm,
                BindFlags = BindFlags.ShaderResource,
                Usage = Usage.Default
            }, new TextureData
            {
                SubResources = new[] { new TextureSubResData { Data = (nint)p, Stride = (ulong)(img.Width * 4) } }
            });
        }
    }

    private void GrowBuffers(int count)
    {
        _origins?.Dispose();
        _origins = _gfx.Device!.CreateBuffer(new()
        {
            Name = "ChunkOrigins", Usage = Usage.Default,
            BindFlags = BindFlags.VertexBuffer, Size = (ulong)count * (ulong)sizeof(Vector4)
        });
        _indirect?.Dispose();
        // Dynamic + Map, как у буфера констант: без UpdateBuffer и без переходов состояния между записью и индирект-чтением
        _indirect = _gfx.Device!.CreateBuffer(new()
        {
            Name = "ChunkIndirect", Usage = Usage.Dynamic,
            BindFlags = BindFlags.IndirectDrawArgs, CPUAccessFlags = CpuAccessFlags.Write,
            Size = (ulong)count * (ulong)sizeof(DrawIndexedCmd)
        });
        _capacity = count;
    }

    public void Draw(FrameContext frame, ReadOnlySpan<Vector4> origins, ReadOnlySpan<DrawIndexedCmd> draws)
    {
        var ctx = _gfx.Context!;
        if (draws.Length > _capacity) GrowBuffers(Math.Max(_capacity * 2, draws.Length));

        fixed (Vector4* p = origins)
            ctx.UpdateBuffer(_origins, 0, (ulong)(origins.Length * sizeof(Vector4)), (IntPtr)p,
                             ResourceStateTransitionMode.Transition);

        var cb = ctx.MapBuffer<ChunkConstants>(_constants, MapType.Write, MapFlags.Discard);
        cb[0] = new ChunkConstants
        {
            ViewProjection = frame.ViewProjection,
            Fade = new Vector4(AppClock.Seconds, 0.7f, 24, 0),
            AtlasInfo = new Vector4(Atlas.Columns, Atlas.Rows, Atlas.Width, Atlas.Height)
        };
        ctx.UnmapBuffer(_constants, MapType.Write);
        ctx.SetPipelineState(_pso);
        ctx.CommitShaderResources(_srb, ResourceStateTransitionMode.Transition);

        // арены привязываются ОДИН раз на все чанки
        ctx.SetVertexBuffers(0, [_vtx.Buffer, _origins], [0ul, 0ul],
                             ResourceStateTransitionMode.Transition, SetVertexBuffersFlags.Reset);
        ctx.SetIndexBuffer(_idx.Buffer, 0, ResourceStateTransitionMode.Transition);

        if (Mode == DrawMode.Direct)
        {
            for (int i = 0; i < draws.Length; i++)
            {
                ref readonly var d = ref draws[i];
                ctx.DrawIndexed(new DrawIndexedAttribs
                {
                    IndexType = Diligent.ValueType.UInt16,
                    NumIndices = d.IndexCount,
                    FirstIndexLocation = d.FirstIndex,
                    BaseVertex = (uint)d.BaseVertex,
                    FirstInstanceLocation = d.FirstInstance,
                    NumInstances = 1,
                });
            }
            return;
        }

        // аргументы всех чанков — в динамический буфер (один раз за кадр), по тому же пути, что и константы
        var ib = ctx.MapBuffer<DrawIndexedCmd>(_indirect, MapType.Write, MapFlags.Discard);
        for (int i = 0; i < draws.Length; i++) ib[i] = draws[i];
        ctx.UnmapBuffer(_indirect, MapType.Write);

        if (Mode == DrawMode.IndirectMulti)
        {
            // один вызов на все чанки: DrawCount > 1, шаг аргументов = DrawIndexedCmd
            ctx.DrawIndexedIndirect(new DrawIndexedIndirectAttribs
            {
                IndexType = Diligent.ValueType.UInt16,
                AttribsBuffer = _indirect,
                DrawArgsOffset = 0,
                DrawCount = (uint)draws.Length,
                DrawArgsStride = (uint)sizeof(DrawIndexedCmd),
            });
        }
        else
        {
            // тест: по одному indirect-вызову на чанк, каждый читает свою запись аргументов
            for (int i = 0; i < draws.Length; i++)
            {
                ctx.DrawIndexedIndirect(new DrawIndexedIndirectAttribs
                {
                    IndexType = Diligent.ValueType.UInt16,
                    AttribsBuffer = _indirect,
                    DrawArgsOffset = (ulong)i * (ulong)sizeof(DrawIndexedCmd),
                    DrawCount = 1,
                    DrawArgsStride = (uint)sizeof(DrawIndexedCmd),
                });
            }
        }
    }

    public void Dispose()
    {
        _origins.Dispose();
        _indirect.Dispose();
        _atlas.Dispose();
        _constants.Dispose();
        _srb.Dispose();
        _pso.Dispose();
    }
}
