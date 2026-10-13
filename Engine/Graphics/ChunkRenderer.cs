using System.Numerics;
using System.Runtime.InteropServices;
using Diligent;
using Engine;
using Engine.Graphics.Memory;
using Engine.Graphics.Shaders;
using Engine.Graphics.Shaders.Generated;
using Engine.Voxels;

namespace Engine.Graphics;

/// <summary>
/// Рисует все видимые чанки из двух больших арен (вершины/индексы) одним набором состояний.
/// origin каждого чанка идёт вторым вершинным потоком (per-instance), поэтому на чанк — один DrawIndexed без смены констант.
/// </summary>
internal sealed unsafe class ChunkRenderer : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    private struct ChunkConstants { public Matrix4x4 ViewProjection; public Vector4 Fade; } // x=время, y=длительность, z=подъём
    private readonly GraphicsContext _gfx;
    private readonly GpuArena _vtx, _idx;
    private readonly IPipelineState _pso;
    private readonly IShaderResourceBinding _srb;
    private readonly IBuffer _constants;
    private IBuffer _origins = null!;
    private int _capacity;

    public ChunkRenderer(GraphicsContext gfx, GpuArena vtx, GpuArena idx)
    {
        (_gfx, _vtx, _idx) = (gfx, vtx, idx);
        var device = gfx.Device!;
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
                ResourceLayout = new() { DefaultVariableType = ShaderResourceVariableType.Static }
            },
            Vs = vs,
            Ps = ps,
            GraphicsPipeline = new()
            {
                InputLayout = new()
                {
                    LayoutElements = new[]
                    {
                        // слот 0: VoxelVertex (8 байт) = PosFace(4×u8) + UvBlock(4×u8)
                        new LayoutElement { InputIndex = 0, BufferSlot = 0, NumComponents = 4, ValueType = Diligent.ValueType.UInt8, IsNormalized = false },
                        new LayoutElement { InputIndex = 1, BufferSlot = 0, NumComponents = 4, ValueType = Diligent.ValueType.UInt8, IsNormalized = false },
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

        GrowOrigins(1024);
    }

    private void GrowOrigins(int count)
    {
        _origins?.Dispose();
        _origins = _gfx.Device!.CreateBuffer(new()
        {
            Name = "ChunkOrigins", Usage = Usage.Default,
            BindFlags = BindFlags.VertexBuffer, Size = (ulong)count * (ulong)sizeof(Vector4)
        });
        _capacity = count;
    }

    public void Draw(FrameContext frame, ReadOnlySpan<Vector4> origins, ReadOnlySpan<DrawIndexedCmd> draws)
    {
        var ctx = _gfx.Context!;
        if (draws.Length > _capacity) GrowOrigins(Math.Max(_capacity * 2, draws.Length));

        fixed (Vector4* p = origins)
            ctx.UpdateBuffer(_origins, 0, (ulong)(origins.Length * sizeof(Vector4)), (IntPtr)p,
                             ResourceStateTransitionMode.Transition);

        var cb = ctx.MapBuffer<ChunkConstants>(_constants, MapType.Write, MapFlags.Discard);
        cb[0] = new ChunkConstants
        {
            ViewProjection = frame.ViewProjection,
            Fade = new Vector4(AppClock.Seconds, 0.7f, 24, 0)
        };
        ctx.UnmapBuffer(_constants, MapType.Write);
        ctx.SetPipelineState(_pso);
        ctx.CommitShaderResources(_srb, ResourceStateTransitionMode.Transition);

        // арены привязываются ОДИН раз на все чанки
        ctx.SetVertexBuffers(0, [_vtx.Buffer, _origins], [0ul, 0ul],
                             ResourceStateTransitionMode.Transition, SetVertexBuffersFlags.Reset);
        ctx.SetIndexBuffer(_idx.Buffer, 0, ResourceStateTransitionMode.Transition);

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
    }

    public void Dispose()
    {
        _origins.Dispose();
        _constants.Dispose();
        _srb.Dispose();
        _pso.Dispose();
    }
}
