using Engine.Graphics.Memory;
using Engine.Rendering;

namespace Engine.Graphics;

/// <summary>Реализация IMeshSink поверх двух GPU-арен (вершины и индексы).</summary>
public sealed class GpuMeshSink : IMeshSink
{
    private readonly IRenderDevice _device;
    private readonly GpuArena _vtx, _idx;

    public GpuMeshSink(IRenderDevice device, GpuArena vtx, GpuArena idx) => (_device, _vtx, _idx) = (device, vtx, idx);

    public bool TryUpload(ReadOnlySpan<byte> vertices, int vertexCount, ReadOnlySpan<byte> indices, int indexCount, out GpuRange range)
    {
        range = default;
        int vo = _vtx.Allocate(vertexCount);
        if (vo < 0) return false;
        int io = _idx.Allocate(indexCount);
        if (io < 0) { _vtx.Free(vo, vertexCount); return false; }

        _vtx.Upload<byte>(_device, vo, vertices);
        _idx.Upload<byte>(_device, io, indices);
        range = new GpuRange(vo, io, vertexCount, indexCount);
        return true;
    }

    public void Release(GpuRange range)
    {
        _vtx.Free(range.VertexOffset, range.VertexCount);
        _idx.Free(range.IndexOffset, range.IndexCount);
    }
}
