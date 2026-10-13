using System.Diagnostics;
using Engine.Core;
using Engine.ECS.Components;
using Engine.ECS.Markers;
using Engine.Memory;
using Engine.Voxels;
using Engine.Voxels.Meshing;
using Friflo.Engine.ECS;
using Friflo.Engine.ECS.Systems;

namespace Engine.ECS.Systems;

/// <summary>
/// Меширует чанки с Generated+Dirty, у которых ГОТОВЫ ВСЕ соседи (см. NeighborSampler.Load):
/// чанк никогда не строится по данным «соседа, которого ещё нет», поэтому на стыках не бывает лишних стенок и кривого AO.
///  * кандидаты отбираются по приоритету (ближние и по взгляду — первыми), а не в порядке хранения;
///  * пачками параллельно: у каждого слота свой GreedyMesher и NeighborSampler (пул в это время только читается);
///  * чанк, целиком заполненный и окружённый такими же, граней не имеет — меширование пропускается;
///  * изменения компонентов/тегов — через CommandBuffer;
///  * если с прошлого скана ничего не поменялось (PipelineState.Version) — заблокированные чанки не пересматриваются.
/// </summary>
internal sealed class ChunkMeshingSystem : QuerySystem<ChunkCoord, ChunkData, Mesh>
{
    private static readonly int SlotCount = Math.Clamp(Environment.ProcessorCount * 2, 8, 32);
    private static readonly int Candidates = SlotCount * 4;

    private static readonly (int X, int Y, int Z)[] FaceNeighbors =
        [(-1, 0, 0), (1, 0, 0), (0, -1, 0), (0, 1, 0), (0, 0, -1), (0, 0, 1)];

    private struct Cand
    {
        public int Id, X, Y, Z;
        public Mesh Mesh;                      // копия компонента (записывается обратно через CommandBuffer)
        public PoolHandle<ushort> Data;
    }

    private sealed class Slot
    {
        public readonly GreedyMesher Mesher = new();
        public readonly NeighborSampler Sampler;
        public int Id;
        public Mesh Mesh;
        public PoolHandle<ushort> Data;
        public int VertexCount, IndexCount;    // результат

        public Slot(MemoryPool pool)
        {
            Sampler = new NeighborSampler(pool);
            Mesher.OutsideSampler = Sampler.Sample;   // делегат создаётся один раз
        }
    }

    private readonly MemoryPool _pool;
    private readonly ChunkMap _map;
    private readonly FloatingOrigin _origin;
    private readonly WorldSettings _settings;
    private readonly PipelineState _state;
    private readonly Slot[] _slots;
    private readonly NeighborSampler _probe;                       // проверка готовности соседей при отборе
    private readonly Cand[] _picked = new Cand[Candidates];
    private readonly PriorityQueue<Cand, float> _heap = new();
    private readonly ParallelOptions _par = new() { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1) };
    private readonly Action<int> _work;
    private int _pickedCount;
    private int _seenVersion = -1;
    private bool _leftover;

    public ChunkMeshingSystem(MemoryPool pool, ChunkMap map, FloatingOrigin origin, WorldSettings settings, PipelineState state)
    {
        (_pool, _map, _origin, _settings, _state) = (pool, map, origin, settings, state);
        _slots = new Slot[SlotCount];
        for (int i = 0; i < _slots.Length; i++) _slots[i] = new Slot(pool);
        _probe = new NeighborSampler(pool);
        _work = MeshOne;
        Filter.AllTags(Tags.Get<Generated, Dirty>());
    }

    protected override void OnUpdate()
    {
        // ничего нового и не осталось недоделанного → ждущим соседей чанкам ждать нечего
        if (_state.Version == _seenVersion && !_leftover) return;
        _seenVersion = _state.Version;
        _leftover = false;

        SelectBest();
        if (_pickedCount == 0) return;

        var ecb = CommandBuffer;
        long deadline = Stopwatch.GetTimestamp() + (long)(_settings.MeshBudgetMs * Stopwatch.Frequency / 1000.0);

        for (int start = 0; start < _pickedCount; start += SlotCount)
        {
            int n = Math.Min(SlotCount, _pickedCount - start);
            int filled = 0;
            for (int i = 0; i < n; i++)
            {
                ref var c = ref _picked[start + i];
                var s = _slots[filled];
                if (!s.Sampler.Load(_map, c.X, c.Y, c.Z)) continue;   // подстраховка: состояние в кадре не менялось
                s.Id = c.Id; s.Mesh = c.Mesh; s.Data = c.Data;
                filled++;
            }
            if (filled > 0) Flush(filled, ecb);

            if (start + n < _pickedCount && Stopwatch.GetTimestamp() >= deadline) { _leftover = true; break; }
        }
    }

    /// <summary>Отбирает до Candidates лучших ГОТОВЫХ к меширо­ванию чанков; _picked[0] — самый приоритетный.</summary>
    private void SelectBest()
    {
        _heap.Clear();
        _pickedCount = 0;

        var prio = new ChunkPriority(_origin);
        int rd = _state.Radius;
        long r2 = (long)rd * rd;            // дальше rd чанк не рисуется, и соседей у него может не быть — не мешим

        foreach (var (coords, datas, meshes, entities) in Query.Chunks)
        {
            var cs = coords.Span;
            var ds = datas.Span;
            var ms = meshes.Span;
            for (int i = 0; i < entities.Length; i++)
            {
                int dx = cs[i].X - _state.CenterX, dz = cs[i].Z - _state.CenterZ;
                if ((long)dx * dx + (long)dz * dz > r2) continue;

                float p = prio.Of(cs[i].X, cs[i].Y, cs[i].Z);
                if (_heap.Count >= Candidates)
                {
                    _heap.TryPeek(out _, out float negWorst);
                    if (p >= -negWorst) continue;                       // хуже худшего из отобранных
                }
                if (!_probe.Load(_map, cs[i].X, cs[i].Y, cs[i].Z)) continue;   // соседи не готовы

                var cand = new Cand { Id = entities[i], X = cs[i].X, Y = cs[i].Y, Z = cs[i].Z, Mesh = ms[i], Data = ds[i].Data };
                if (_heap.Count < Candidates) _heap.Enqueue(cand, -p);
                else _heap.EnqueueDequeue(cand, -p);
            }
        }

        int count = _heap.Count;
        _pickedCount = count;
        if (count == Candidates) _leftover = true;                      // возможно, есть ещё готовые
        for (int k = count - 1; k >= 0; k--) _picked[k] = _heap.Dequeue();   // худший вышел первым → с конца
    }

    private void Flush(int n, CommandBuffer ecb)
    {
        if (n == 1) MeshOne(0);
        else Parallel.For(0, n, _par, _work);

        // дальше пул меняется (Free/Allocate) — только главный поток
        for (int i = 0; i < n; i++)
        {
            var s = _slots[i];
            ref var mesh = ref s.Mesh;

            _pool.FreeIfAlive(ref mesh.Vertices);
            _pool.FreeIfAlive(ref mesh.Indices);

            if (s.IndexCount > 0)
            {
                mesh.Vertices = _pool.AllocateFrom<VoxelVertex>(s.Mesher.Vertices.AsSpan(0, s.VertexCount)).Handle;
                mesh.Indices  = _pool.AllocateFrom<ushort>(s.Mesher.Indices.AsSpan(0, s.IndexCount)).Handle;
            }
            mesh.VertexCount = s.VertexCount;
            mesh.IndexCount  = s.IndexCount;
            mesh.Version++;

            ecb.AddComponent(s.Id, mesh);
            ecb.RemoveTag<Dirty>(s.Id);
            ecb.AddTag<NeedsUpload>(s.Id);
        }
    }

    // выполняется в потоках пула: только чтение из MemoryPool
    private void MeshOne(int i)
    {
        var s = _slots[i];
        s.VertexCount = s.IndexCount = 0;
        if (!_pool.IsAlive(s.Data.Handle)) return;                // воздух

        var voxels = _pool.GetSpan(s.Data);
        if (IsBuried(s, voxels)) return;                          // граней нет

        s.Mesher.Mesh(voxels);
        s.VertexCount = s.Mesher.VertexCount;
        s.IndexCount  = s.Mesher.IndexCount;
    }

    /// <summary>Чанк полностью твёрдый и все 6 соседей по граням тоже — видимых граней быть не может.</summary>
    private bool IsBuried(Slot s, ReadOnlySpan<ushort> voxels)
    {
        if (voxels.Contains((ushort)0)) return false;
        foreach (var (dx, dy, dz) in FaceNeighbors)
        {
            var h = s.Sampler.Neighbor(dx, dy, dz);
            if (!_pool.IsAlive(h.Handle) || _pool.GetSpan(h).Contains((ushort)0)) return false;
        }
        return true;
    }
}

internal sealed class NeighborSampler
{
    private readonly MemoryPool _pool;
    private readonly PoolHandle<ushort>[] _n = new PoolHandle<ushort>[27];   // 3×3×3

    public NeighborSampler(MemoryPool pool) => _pool = pool;

    /// <summary>
    /// false → мешить рано: какой-то сосед ещё не создан или не сгенерирован.
    /// «Воздухом» считается ТОЛЬКО то, что выше/ниже границ мира по Y; отсутствующий горизонтальный сосед — это «ещё не загружен»,
    /// а не «пусто», иначе на стыке чанков строились бы лишние стенки и неверный AO.
    /// </summary>
    public bool Load(ChunkMap map, int cx, int cy, int cz)
    {
        for (int dz = -1; dz <= 1; dz++)
        for (int dy = -1; dy <= 1; dy++)
        for (int dx = -1; dx <= 1; dx++)
        {
            int i = (dx + 1) + (dy + 1) * 3 + (dz + 1) * 9;
            _n[i] = default;
            if ((dx | dy | dz) == 0) continue;

            int ny = cy + dy;
            if (ny < WorldLimits.MinChunkY || ny > WorldLimits.MaxChunkY) continue;     // за краем мира по Y = воздух

            if (!map.TryGet(cx + dx, ny, cz + dz, out var ne)) return false;            // сосед ещё не создан
            if (!ne.Tags.Has<Generated>()) return false;
            _n[i] = ne.GetComponent<ChunkData>().Data;
        }
        return true;
    }

    public PoolHandle<ushort> Neighbor(int dx, int dy, int dz) => _n[(dx + 1) + (dy + 1) * 3 + (dz + 1) * 9];

    // координаты в системе ЭТОГО чанка, вне 0..15 (максимум на 1 в каждую сторону)
    public ushort Sample(int x, int y, int z)
    {
        int dx = x < 0 ? -1 : x >= Chunk.Size ? 1 : 0;
        int dy = y < 0 ? -1 : y >= Chunk.Size ? 1 : 0;
        int dz = z < 0 ? -1 : z >= Chunk.Size ? 1 : 0;
        var h = _n[(dx + 1) + (dy + 1) * 3 + (dz + 1) * 9];
        if (!_pool.IsAlive(h.Handle)) return 0;
        return _pool.GetSpan(h)[Chunk.Index(x - dx * Chunk.Size, y - dy * Chunk.Size, z - dz * Chunk.Size)];
    }
}
