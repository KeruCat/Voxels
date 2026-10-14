using System.Numerics;
using System.Runtime.InteropServices;
using Engine;
using Engine.Graphics;
using Engine.Graphics.Images;
using Engine.Graphics.Memory;
using Engine.Rendering;
using Game.Voxels;

namespace Game.Render;

/// <summary>Способы отрисовки чанков.</summary>
public enum DrawMode
{
    Direct,           // цикл DrawIndexed (проверенный путь)
    IndirectSingle,   // DrawIndexedIndirect по одному чанку (проверка DrawCount/stride)
    IndirectMulti,    // один DrawIndexedIndirect на все чанки
}

/// <summary>
/// Рисует видимые чанки через IRenderDevice: арены вершин и индексов, origin каждого чанка — вторым потоком (per-instance).
/// Diligent здесь не используется: только интерфейсы движка рендера.
/// </summary>
internal sealed unsafe class VoxelRenderer : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    private struct ChunkConstants
    {
        public Matrix4x4 ViewProjection;
        public Vector4 Fade;        // x=время, y=длительность появления
        public Vector4 AtlasInfo;   // x=колонок, y=строк, z=ширина атласа px, w=высота атласа px
    }

    private readonly IRenderDevice _device;
    private readonly GpuArena _vtx, _idx;
    private readonly IPipeline _pipeline;
    private readonly ITexture2D _atlas;
    private IGpuBuffer? _origins;
    private IGpuBuffer? _indirect;     // аргументы DrawIndexedIndirect: DrawIndexedCommand (20 байт)
    private int _capacity;

    /// <summary>Способ отрисовки. Нужен для сравнения режимов на одной сцене.</summary>
    public DrawMode Mode { get; set; } = DrawMode.IndirectMulti;

    public VoxelRenderer(IRenderDevice device, GpuArena vtx, GpuArena idx, string atlasPath)
    {
        (_device, _vtx, _idx) = (device, vtx, idx);
        _atlas = CreateAtlas(device, atlasPath);

        // имена шейдеров — ключи словаря, который передаётся устройству при создании (см. GameApp)
        _pipeline = device.CreatePipeline(new PipelineDesc
        {
            Name = "Voxel PSO",
            VertexShader = "voxel.vs",
            PixelShader = "voxel.ps",
            Inputs =
            [
                // слот 0: VoxelVertex (8 байт) = PosFace(4×u8) + Packed(u32)
                new VertexInput(0, 0, VertexFormat.UInt8x4),
                new VertexInput(1, 0, VertexFormat.UInt32),
                // слот 1: origin чанка (xyz) + время начала показа (w), один на инстанс
                new VertexInput(2, 1, VertexFormat.Float32x4, PerInstance: true),
            ],
            PixelTextures = ["atlas"],
            ConstantBuffers = ["constants"],
            TextureFilter = SamplerFilter.Point,     // пиксель-арт без сглаживания
        });

        GrowBuffers(1024);
    }

    /// <summary>Загружает Blocks.png (RGBA8). Размер должен совпасть с Atlas.Width × Atlas.Height.</summary>
    private static ITexture2D CreateAtlas(IRenderDevice device, string path)
    {
        var img = PngDecoder.Decode(File.ReadAllBytes(path));
        if (img.Width != Atlas.Width || img.Height != Atlas.Height)
            throw new InvalidDataException($"Atlas must be {Atlas.Width}x{Atlas.Height}, got {img.Width}x{img.Height}: {path}");

        return device.CreateTexture(new TextureDesc("Block atlas", img.Width, img.Height, TextureFormat.Rgba8), img.Rgba);
    }

    private void GrowBuffers(int count)
    {
        _origins?.Dispose();
        _origins = _device.CreateBuffer(new GpuBufferDesc("ChunkOrigins", (ulong)count * (ulong)sizeof(Vector4),
                                                          BufferUsage.Vertex, BufferAccess.GpuOnly));
        _indirect?.Dispose();
        _indirect = _device.CreateBuffer(new GpuBufferDesc("ChunkIndirect", (ulong)count * (ulong)sizeof(DrawIndexedCommand),
                                                           BufferUsage.Indirect, BufferAccess.CpuWrite));
        _capacity = count;
    }

    /// <summary>Записывает команды отрисовки видимых чанков в текущий кадр.</summary>
    public void Draw(IRenderCommands cmd, FrameContext frame, ReadOnlySpan<Vector4> origins, ReadOnlySpan<DrawIndexedCommand> draws)
    {
        if (draws.Length > _capacity) GrowBuffers(Math.Max(_capacity * 2, draws.Length));

        cmd.SetPipeline(_pipeline);
        cmd.SetConstants("constants", new ChunkConstants
        {
            ViewProjection = frame.ViewProjection,
            Fade = new Vector4(AppClock.Seconds, 0.7f, 24, 0),
            AtlasInfo = new Vector4(Atlas.Columns, Atlas.Rows, Atlas.Width, Atlas.Height),
        });
        cmd.SetTexture("atlas", _atlas);

        // origin всех видимых чанков — в буфер вершин слота 1 (per-instance)
        cmd.Write(_origins!, 0, origins);

        // арены привязываются один раз на все чанки
        cmd.SetVertexBuffer(0, _vtx.Buffer);
        cmd.SetVertexBuffer(1, _origins!);
        cmd.SetIndexBuffer(_idx.Buffer, IndexFormat.UInt16);

        if (Mode == DrawMode.Direct)
        {
            for (int i = 0; i < draws.Length; i++)
                cmd.DrawIndexed(draws[i]);
            return;
        }

        // аргументы всех чанков — в буфер аргументов (один раз за кадр)
        cmd.Write(_indirect!, 0, draws);

        uint stride = (uint)sizeof(DrawIndexedCommand);
        if (Mode == DrawMode.IndirectMulti)
        {
            // один вызов на все чанки
            cmd.DrawIndexedIndirect(_indirect!, 0, (uint)draws.Length, stride);
        }
        else
        {
            // тест: по одному indirect-вызову на чанк
            for (int i = 0; i < draws.Length; i++)
                cmd.DrawIndexedIndirect(_indirect!, (ulong)i * stride, 1, stride);
        }
    }

    public void Dispose()
    {
        _origins?.Dispose();
        _indirect?.Dispose();
        _atlas.Dispose();
        _pipeline.Dispose();
    }
}
