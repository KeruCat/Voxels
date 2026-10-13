using Engine.Graphics.Memory;

namespace Engine.Graphics;

/// <summary>Реализация IMeshSink поверх двух GPU-арен (вершины и индексы).</summary>
internal sealed class GpuMeshSink : IMeshSink
{
    private readonly GraphicsContext _gfx;
    private readonly GpuArena _vtx, _idx;

    internal GpuMeshSink(GraphicsContext gfx, GpuArena vtx, GpuArena idx) => (_gfx, _vtx, _idx) = (gfx, vtx, idx);

    public bool TryUpload(ReadOnlySpan<byte> vertices, int vertexCount, ReadOnlySpan<byte> indices, int indexCount, out GpuRange range)
    {
        range = default;
        int vo = _vtx.Allocate(vertexCount);
        if (vo < 0) return false;
        int io = _idx.Allocate(indexCount);
        if (io < 0) { _vtx.Free(vo, vertexCount); return false; }

        var ctx = _gfx.Context!;
        _vtx.Upload<byte>(ctx, vo, vertices);
        _idx.Upload<byte>(ctx, io, indices);
        range = new GpuRange(vo, io, vertexCount, indexCount);
        return true;
    }

    public void Release(GpuRange range)
    {
        _vtx.Free(range.VertexOffset, range.VertexCount);
        _idx.Free(range.IndexOffset, range.IndexCount);
    }
}
