using System.Diagnostics;
using Engine.Core;
using Engine.ECS.Components;
using Engine.ECS.Markers;
using Engine.Graphics;
using Engine.Graphics.Memory;
using Engine.Memory;
using Engine.Voxels;
using Friflo.Engine.ECS;
using Friflo.Engine.ECS.Systems;

namespace Engine.ECS.Systems;

/// <summary>
/// Заливает меши (NeedsUpload) в общие GPU-арены.
///  * за кадр отбираются лучшие по приоритету (ближние и по взгляду — первыми), а не в порядке хранения;
///  * компоненты и теги меняются через CommandBuffer; Playback — сразу после обработки,
///    чтобы culling/рендер этого же кадра видели актуальные GpuMesh;
///  * ограничено и числом чанков, и временем кадра.
/// </summary>
internal sealed class ChunkUploadSystem : QuerySystem<ChunkCoord, Mesh, GpuMesh>
{
    private struct Cand
    {
        public int Id;
        public Mesh Mesh;
        public GpuMesh Gpu;
    }

    private readonly MemoryPool _pool;
    private readonly GraphicsContext _gfx;
    private readonly GpuArena _vtx, _idx;
    private readonly FloatingOrigin _origin;
    private readonly WorldSettings _settings;
    private readonly PriorityQueue<Cand, float> _heap = new();
    private Cand[] _picked = new Cand[64];

    public ChunkUploadSystem(MemoryPool pool, GraphicsContext gfx, GpuArena vtx, GpuArena idx,
                             WorldSettings settings, FloatingOrigin origin)
    {
        (_pool, _gfx, _vtx, _idx, _settings, _origin) = (pool, gfx, vtx, idx, settings, origin);
        Filter.AllTags(Tags.Get<NeedsUpload>());
    }

    protected override void OnUpdate()
    {
        int k = Math.Max(1, _settings.UploadPerFrame);
        if (_picked.Length < k) _picked = new Cand[k];

        // 1) лучшие k кандидатов
        _heap.Clear();
        var prio = new ChunkPriority(_origin);
        foreach (var (coords, meshes, gpus, entities) in Query.Chunks)
        {
            var cs = coords.Span;
            var ms = meshes.Span;
            var gs = gpus.Span;
            for (int i = 0; i < entities.Length; i++)
            {
                float p = prio.Of(cs[i].X, cs[i].Y, cs[i].Z);
                if (_heap.Count >= k)
                {
                    _heap.TryPeek(out _, out float negWorst);
                    if (p >= -negWorst) continue;
                }
                var cand = new Cand { Id = entities[i], Mesh = ms[i], Gpu = gs[i] };
                if (_heap.Count < k) _heap.Enqueue(cand, -p);
                else _heap.EnqueueDequeue(cand, -p);
            }
        }
        int count = _heap.Count;
        if (count == 0) return;
        for (int j = count - 1; j >= 0; j--) _picked[j] = _heap.Dequeue();   // лучший — первый

        // 2) заливка по порядку, пока есть время и место в аренах
        var ctx = _gfx.Context!;
        var ecb = CommandBuffer;
        long deadline = Stopwatch.GetTimestamp() + (long)(_settings.UploadBudgetMs * Stopwatch.Frequency / 1000.0);

        for (int j = 0; j < count; j++)
        {
            if (j > 0 && Stopwatch.GetTimestamp() >= deadline) break;      // остальные — в следующем кадре

            ref var c = ref _picked[j];
            ref var mesh = ref c.Mesh;
            ref var gpu = ref c.Gpu;

            if (gpu.IndexCount > 0)                                         // освободить прежнее размещение
            {
                _vtx.Free(gpu.VertexOffset, gpu.VertexCount);
                _idx.Free(gpu.IndexOffset, gpu.IndexCount);
            }
            gpu = new GpuMesh { Age = -1f };

            if (mesh.IndexCount > 0)
            {
                int vo = _vtx.Allocate(mesh.VertexCount);
                if (vo < 0) { ecb.AddComponent(c.Id, gpu); break; }         // арена полна — повторим в следующем кадре
                int io = _idx.Allocate(mesh.IndexCount);
                if (io < 0) { _vtx.Free(vo, mesh.VertexCount); ecb.AddComponent(c.Id, gpu); break; }

                _vtx.Upload<byte>(ctx, vo, _pool.GetBytes(mesh.Vertices));
                _idx.Upload<byte>(ctx, io, _pool.GetBytes(mesh.Indices));

                gpu = new GpuMesh { VertexOffset = vo, IndexOffset = io,
                                    VertexCount = mesh.VertexCount, IndexCount = mesh.IndexCount, Age = -1f };

                _pool.FreeIfAlive(ref mesh.Vertices);                       // CPU-копия больше не нужна
                _pool.FreeIfAlive(ref mesh.Indices);
                mesh.VertexCount = mesh.IndexCount = 0;
            }

            ecb.AddComponent(c.Id, mesh);
            ecb.AddComponent(c.Id, gpu);
            ecb.RemoveTag<NeedsUpload>(c.Id);
        }

        ecb.Playback();   // применяем сразу: culling/рендер этого кадра должны видеть новые GpuMesh
    }
}
