using System.Numerics;
using Engine.Core;
using Engine.ECS.Components;
using Engine.ECS.Markers;
using Engine.Graphics;
using Engine.Voxels;
using Friflo.Engine.ECS;
using Friflo.Engine.ECS.Systems;

namespace Engine.ECS.Systems;

internal sealed class ChunkRenderSystem : QuerySystem<ChunkCoord, GpuMesh>
{
    private readonly ChunkRenderer _renderer;
    private readonly FrameContext _frame;
    private readonly FloatingOrigin _origin;
    private readonly WorldStats _stats;
    private DrawIndexedCmd[] _draws = new DrawIndexedCmd[1024];
    private Vector4[] _origins = new Vector4[1024];

    public ChunkRenderSystem(ChunkRenderer renderer, FrameContext frame, FloatingOrigin origin, WorldStats stats)
    {
        (_renderer, _frame, _origin, _stats) = (renderer, frame, origin, stats);
        Filter.AllTags(Tags.Get<Visible>());
    }

    protected override void OnUpdate()
    {
        int n = 0;
        foreach (var (coords, meshes, entities) in Query.Chunks)
        {
            var c = coords.Span; var g = meshes.Span;
            if (n + entities.Length > _draws.Length)
            {
                int cap = Math.Max(_draws.Length * 2, n + entities.Length);
                Array.Resize(ref _draws, cap); Array.Resize(ref _origins, cap);
            }
            for (int i = 0; i < entities.Length; i++, n++)
            {
                // origin чанка в render-пространстве: целая разность чанков * 16 — всегда маленькое точное число
                var p = new Vector3(c[i].X - _origin.X, c[i].Y - _origin.Y, c[i].Z - _origin.Z) * Chunk.Size;
                _origins[n] = new Vector4(p, g[i].Age);
                _draws[n] = new DrawIndexedCmd
                {
                    IndexCount = (uint)g[i].IndexCount, InstanceCount = 1,
                    FirstIndex = (uint)g[i].IndexOffset, BaseVertex = g[i].VertexOffset,
                    FirstInstance = (uint)n,
                };
            }
        }
        _stats.Drawn = n;
        if (n > 0) _renderer.Draw(_frame, _origins.AsSpan(0, n), _draws.AsSpan(0, n));
    }
}
