using System.Diagnostics;
using System.Numerics;
using Engine.Graphics;
using Engine.Jobs;
using Engine.Memory;
using Game.Voxels;
using Game.Voxels.Generators;
using Game.Voxels.Meshing;

namespace Game.World;

/// <summary>
/// Вокселный мир: хранение, стриминг вокруг игрока и конвейер
///   spawn → generate → (все соседи готовы) → mesh → upload.
///
/// Как устроено «готовность», без опроса чанков каждый кадр:
///  * у каждого чанка есть счётчик PendingNeighbors — сколько из 26 соседей ещё не сгенерированы;
///  * когда чанк генерируется, он уменьшает счётчик у своих соседей; сосед, у которого счётчик дошёл до 0
///    и который сам сгенерирован, кладётся в очередь меширования;
///  * очереди содержат только то, что можно делать прямо сейчас. Каждый шаг берёт из своей очереди k лучших по приоритету.
///
/// Инвариант счётчика: PendingNeighbors(K) = число позиций-соседей P (внутри мира по Y), для которых
/// НЕ (чанк P существует И сгенерирован). Отсутствующий чанк считается «ещё не готов».
/// </summary>
public sealed class VoxelWorld : IDisposable
{
    private static readonly int GenBatch = Math.Clamp(Environment.ProcessorCount * 4, 16, 64);
    private static readonly int MeshBatch = Math.Clamp(Environment.ProcessorCount * 2, 8, 32);
    private static readonly ChunkKey[] NeighborOffsets = BuildNeighborOffsets();

    private struct GenJob
    {
        public ChunkKey Key;
        public ushort[]? Buffer;     // переиспользуется между кадрами, не в пуле
        public bool HasVoxels;
    }

    private struct MeshJob
    {
        public ChunkKey Key;
        public PoolHandle<ushort> Voxels;
        public int Slot;
        public int VertexCount, IndexCount;
    }

    private readonly ChunkStore _store = new();
    private readonly MemoryPool _pool;
    private readonly IMeshSink _sink;
    private readonly CpuPool _cpu;
    private readonly WorldSettings _settings;
    private readonly ThreadLocal<IChunkGenerator> _gen;

    private readonly KeyQueue _toGenerate = new(), _toMesh = new(), _toUpload = new();
    private readonly List<ChunkKey> _spawnList = new();   // диск, отсортирован от ближних к дальним
    private int _spawnCursor;

    // воздушные чанки: в хранилище не лежат, но соседи считают их готовыми
    private readonly HashSet<ChunkKey> _air = new();
    private readonly List<ChunkKey> _airScratch = new();
    private const float NoFade = -1e6f;   // время появления в далёком прошлом: чанк сразу полностью виден
    private const int GenSelect = 256;      // сколько лучших выбираем за кадр для генерации
    private const int MeshSelect = 128;     // и для меширования
    private readonly ChunkKey[] _genSel, _meshSel, _uploadSel;
    private readonly GenJob[] _genJobs;
    private readonly MeshJob[] _meshJobs;
    private readonly MeshWorker[] _meshWorkers;

    private readonly JobFn<GenJob> _genFn;
    private readonly JobFn<MeshJob> _meshFn;
    private readonly Func<ChunkKey, bool> _genValid, _meshValid, _uploadValid;

    private bool _hasCenter;
    private int _cx, _cz, _radius = -1;
    private (int Dx, int Dz)[] _discOffsets = [];   // смещения колонок диска, ближние первыми (кэш на радиус)
    private int _genR2;                              // квадрат радиуса диска генерации

    public int ChunkCount => _store.Count;
    public int VisibleCount { get; private set; }

    // ── для отладки: размеры очередей и время шагов последнего кадра (мс) ──
    public int PendingSpawn => _spawnList.Count - _spawnCursor;
    public int AirCount => _air.Count;
    public int PendingGenerate => _toGenerate.Count;
    public int PendingMesh => _toMesh.Count;
    public int PendingUpload => _toUpload.Count;
    public double LastRecenterMs { get; private set; }
    public double LastSpawnMs { get; private set; }
    public double LastGenMs { get; private set; }
    public double LastMeshMs { get; private set; }
    public double LastUploadMs { get; private set; }

    public VoxelWorld(MemoryPool pool, IMeshSink sink, CpuPool cpu, WorldSettings settings, Func<IChunkGenerator> generatorFactory)
    {
        (_pool, _sink, _cpu, _settings) = (pool, sink, cpu, settings);
        _gen = new ThreadLocal<IChunkGenerator>(generatorFactory);     // генератор не потокобезопасен

        _genSel = new ChunkKey[GenSelect];
        _genJobs = new GenJob[GenBatch];
        _meshSel = new ChunkKey[MeshSelect];
        _meshJobs = new MeshJob[MeshBatch];
        _meshWorkers = new MeshWorker[MeshBatch];
        for (int i = 0; i < MeshBatch; i++) _meshWorkers[i] = new MeshWorker(pool);
        _uploadSel = new ChunkKey[Math.Max(1, settings.UploadPerFrame)];

        _genFn = GenerateOne;
        _meshFn = MeshOne;
        _genValid = IsGenPending;
        _meshValid = IsMeshReady;
        _uploadValid = IsUploadPending;
    }

    // ───────────── кадр ─────────────

    /// <summary>Один шаг мира за кадр. playerCx/Cz — колонка игрока; prio — приоритет от его позиции и взгляда.</summary>
    public void Update(int playerCx, int playerCz, ChunkPriority prio)
    {
        int rd = _settings.RenderDistance;
        long t0 = Stopwatch.GetTimestamp();
        bool radiusChanged = !_hasCenter || rd != _radius;
        bool moved = playerCx != _cx || playerCz != _cz;
        if (radiusChanged || moved)
        {
            int ocx = _cx, ocz = _cz;
            (_hasCenter, _cx, _cz) = (true, playerCx, playerCz);
            if (radiusChanged)
            {
                _radius = rd;
                Unload();
                BuildDiscOffsets(rd);
                BuildSpawnFull();
                RescanMeshable();
            }
            else
            {
                Unload();
                BuildSpawnRing(ocx, ocz);     // только новые колонки диска
                RescanRing(ocx, ocz);         // только колонки, которые вошли в радиус показа
            }
        }
        long t1 = Stopwatch.GetTimestamp();

        SpawnStep();
        long t2 = Stopwatch.GetTimestamp();
        GenerateStep(prio);
        long t3 = Stopwatch.GetTimestamp();
        MeshStep(prio);
        long t4 = Stopwatch.GetTimestamp();
        UploadStep(prio);
        long t5 = Stopwatch.GetTimestamp();

        LastRecenterMs = Ms(t0, t1);
        LastSpawnMs = Ms(t1, t2);
        LastGenMs = Ms(t2, t3);
        LastMeshMs = Ms(t3, t4);
        LastUploadMs = Ms(t4, t5);
    }

    private static double Ms(long from, long to) => (to - from) * 1000.0 / Stopwatch.Frequency;

    /// <summary>Пересчитывает видимость по фрустуму и число видимых. Один проход по плотному массиву.</summary>
    public void Cull(IAabbTest test, int originX, int originY, int originZ, float now)
    {
        int count = 0;
        var all = _store.All;
        for (int i = 0; i < all.Length; i++)
        {
            ref var c = ref all[i];
            var min = new Vector3(c.Key.X - originX, c.Key.Y - originY, c.Key.Z - originZ) * Chunk.Size;
            bool vis = c.Gpu.IndexCount > 0 && test.Intersects(min, min + new Vector3(Chunk.Size));
            if (vis && !c.Shown)
            {
                c.Shown = true;
                c.FadeStart = now;
            }
            c.Visible = vis;
            if (vis) count++;
        }
        VisibleCount = count;
    }

    /// <summary>Заполняет инстансы и команды отрисовки для видимых чанков. Размер буферов — не меньше VisibleCount.</summary>
    internal int BuildDraws(int originX, int originY, int originZ, Span<Vector4> origins, Span<DrawIndexedCmd> draws)
    {
        int n = 0;
        var all = _store.All;
        for (int i = 0; i < all.Length; i++)
        {
            ref var c = ref all[i];
            if (!c.Visible) continue;

            var p = new Vector3(c.Key.X - originX, c.Key.Y - originY, c.Key.Z - originZ) * Chunk.Size;
            origins[n] = new Vector4(p, c.FadeStart);
            draws[n] = new DrawIndexedCmd
            {
                IndexCount = (uint)c.Gpu.IndexCount,
                InstanceCount = 1,
                FirstIndex = (uint)c.Gpu.IndexOffset,
                BaseVertex = c.Gpu.VertexOffset,
                FirstInstance = (uint)n,
            };
            n++;
        }
        return n;
    }

    // ───────────── стриминг ─────────────

    /// <summary>Выгрузка: один обратный проход (swap-remove не ломает ещё не просмотренные элементы).</summary>
    private void Unload()
    {
        long r = _radius + 3;
        long r2 = r * r;
        for (int i = _store.Count - 1; i >= 0; i--)
        {
            var key = _store[i].Key;
            long dx = key.X - _cx, dz = key.Z - _cz;
            if (dx * dx + dz * dz <= r2) continue;
            Remove(i);
        }

        PruneAir(r2);
    }

    /// <summary>Забывает воздушные чанки вдали. Соседи, которые считали их готовыми, снова ждут их.</summary>
    private void PruneAir(long r2)
    {
        if (_air.Count == 0) return;
        _airScratch.Clear();
        foreach (var k in _air)
        {
            long dx = k.X - _cx, dz = k.Z - _cz;
            if (dx * dx + dz * dz > r2) _airScratch.Add(k);
        }
        foreach (var k in _airScratch)
        {
            _air.Remove(k);
            foreach (var off in NeighborOffsets)
            {
                var nk = k.Plus(off);
                if (WorldLimits.InY(nk.Y) && _store.TryGetIndex(nk, out int ni)) _store[ni].PendingNeighbors++;
            }
        }
    }

    private void Remove(int index)
    {
        Chunk c = _store.RemoveAt(index);

        // Соседи, которые считали этот чанк «готовым», теперь должны ждать его снова.
        // Если чанк не был сгенерирован, соседи уже считают его «не готовым» — ничего не меняем.
        if (c.Generated)
        {
            foreach (var off in NeighborOffsets)
            {
                var nk = c.Key.Plus(off);
                if (WorldLimits.InY(nk.Y) && _store.TryGetIndex(nk, out int ni)) _store[ni].PendingNeighbors++;
            }
        }

        _pool.FreeIfAlive(ref c.Voxels);
        _pool.FreeIfAlive(ref c.MeshVertices);
        _pool.FreeIfAlive(ref c.MeshIndices);
        if (c.Gpu.IndexCount > 0) _sink.Release(c.Gpu);
    }

    /// <summary>Смещения колонок диска генерации (ближние первыми). Пересчитывается только при смене радиуса.</summary>
    private void BuildDiscOffsets(int rd)
    {
        double genRadius = rd + 1.5;                 // на 1.5 дальше показа: мешеру нужны соседи
        _genR2 = (int)(genRadius * genRadius);
        int reach = rd + 2;

        var list = new List<(int Dx, int Dz, int D2)>();
        for (int dz = -reach; dz <= reach; dz++)
        for (int dx = -reach; dx <= reach; dx++)
        {
            int d2 = dx * dx + dz * dz;
            if (d2 <= _genR2) list.Add((dx, dz, d2));
        }
        list.Sort(static (a, b) => a.D2.CompareTo(b.D2));

        _discOffsets = new (int Dx, int Dz)[list.Count];
        for (int i = 0; i < list.Count; i++) _discOffsets[i] = (list[i].Dx, list[i].Dz);
    }

    private bool InDisc(long dx, long dz) => dx * dx + dz * dz <= _genR2;

    /// <summary>Полная очередь спавна (при смене радиуса).</summary>
    private void BuildSpawnFull()
    {
        _spawnList.Clear();
        _spawnCursor = 0;
        foreach (var (dx, dz) in _discOffsets)
        for (int cy = WorldLimits.MinChunkY; cy <= WorldLimits.MaxChunkY; cy++)
        {
            var key = new ChunkKey(_cx + dx, cy, _cz + dz);
            if (!_store.Contains(key) && !_air.Contains(key)) _spawnList.Add(key);
        }
    }

    /// <summary>
    /// Смена центра на одну-две колонки: не трогаем то, что уже было в диске.
    /// Оставшуюся очередь фильтруем по новому диску, добавляем только колонки, которых не было в старом.
    /// </summary>
    private void BuildSpawnRing(int ocx, int ocz)
    {
        _spawnList.RemoveRange(0, _spawnCursor);
        _spawnCursor = 0;
        int kept = 0;
        for (int i = 0; i < _spawnList.Count; i++)
        {
            var k = _spawnList[i];
            if (InDisc(k.X - _cx, k.Z - _cz)) _spawnList[kept++] = k;
        }
        _spawnList.RemoveRange(kept, _spawnList.Count - kept);

        foreach (var (dx, dz) in _discOffsets)
        {
            if (InDisc(_cx + dx - ocx, _cz + dz - ocz)) continue;     // была в старом диске
            for (int cy = WorldLimits.MinChunkY; cy <= WorldLimits.MaxChunkY; cy++)
            {
                var key = new ChunkKey(_cx + dx, cy, _cz + dz);
                if (!_store.Contains(key) && !_air.Contains(key)) _spawnList.Add(key);
            }
        }
    }

    private void SpawnStep()
    {
        int budget = _settings.SpawnPerFrame;
        while (budget > 0 && _spawnCursor < _spawnList.Count)
        {
            var key = _spawnList[_spawnCursor++];
            if (_store.Contains(key) || _air.Contains(key)) continue;
            AddChunk(key);
            budget--;
        }
    }

    private void AddChunk(ChunkKey key)
    {
        int pending = 0;
        foreach (var off in NeighborOffsets)
        {
            var nk = key.Plus(off);
            if (!WorldLimits.InY(nk.Y)) continue;
            if (!IsReady(nk)) pending++;
        }
        _store.Add(key, pending);
        _toGenerate.Enqueue(key);
    }

    /// <summary>Полный проход (при смене радиуса): готовые чанки в радиусе — в очередь меширования.</summary>
    private void RescanMeshable()
    {
        var all = _store.All;
        for (int i = 0; i < all.Length; i++)
        {
            ref var c = ref all[i];
            if (c.Generated && !c.Meshed && c.PendingNeighbors == 0 && InRadius(c.Key)) _toMesh.Enqueue(c.Key);
        }
    }

    /// <summary>
    /// Смена центра: только колонки, которые вошли в радиус показа. Остальные уже обработаны раньше,
    /// а те, что были в радиусе и ждали соседей, сами попадут в очередь при генерации соседей.
    /// </summary>
    private void RescanRing(int ocx, int ocz)
    {
        long r2 = (long)_radius * _radius;
        foreach (var (dx, dz) in _discOffsets)
        {
            if ((long)dx * dx + (long)dz * dz > r2) continue;                // вне радиуса показа
            if (InRadiusAt(_cx + dx - ocx, _cz + dz - ocz, r2)) continue;   // был в радиусе
            for (int cy = WorldLimits.MinChunkY; cy <= WorldLimits.MaxChunkY; cy++)
            {
                var key = new ChunkKey(_cx + dx, cy, _cz + dz);
                if (_store.TryGetIndex(key, out int i))
                {
                    ref var c = ref _store[i];
                    if (c.Generated && !c.Meshed && c.PendingNeighbors == 0) _toMesh.Enqueue(key);
                }
            }
        }
    }

    private static bool InRadiusAt(long dx, long dz, long r2) => dx * dx + dz * dz <= r2;

    // ───────────── конвейер ─────────────

    private void GenerateStep(ChunkPriority prio)
    {
        int total = _toGenerate.TakeBest(prio, _genSel.Length, _genValid, _genSel);
        long deadline = Deadline(_settings.GenBudgetMs);
        int pos = 0;
        while (pos < total)
        {
            int n = Math.Min(GenBatch, total - pos);
            for (int i = 0; i < n; i++) _genJobs[i].Key = _genSel[pos + i];
            _cpu.Run(_genJobs, n, _genFn);
            for (int i = 0; i < n; i++) ApplyGenerated(i);
            pos += n;
            if (Stopwatch.GetTimestamp() >= deadline) break;
        }
        for (int i = pos; i < total; i++) _toGenerate.Enqueue(_genSel[i]);   // не успели — в очередь
    }

    private void GenerateOne(ref GenJob job)
    {
        job.Buffer ??= new ushort[Chunk.Volume];
        job.HasVoxels = _gen.Value!.Fill(job.Buffer, job.Key.X, job.Key.Y, job.Key.Z);
    }

    private void ApplyGenerated(int i)
    {
        ref var job = ref _genJobs[i];
        if (!_store.TryGetIndex(job.Key, out int idx)) return;      // выгружен, пока считался
        if (_store[idx].Generated) return;                           // дубликат в той же пачке

        if (!job.HasVoxels || !HasSolid(job.Buffer!))
        {
            // воздух не хранится: соседи и так теперь считают его готовым
            _store.RemoveAt(idx);
            _air.Add(job.Key);
            DecrementNeighbors(job.Key);
            return;
        }
        _store[idx].Voxels = _pool.AllocateFrom<ushort>(job.Buffer.AsSpan());
        _store[idx].Generated = true;
        OnGenerated(job.Key, idx);
    }

    /// <summary>Чанк сгенерирован: сам — в меширование, если соседи готовы; соседям — один шаг вычитания.</summary>
    private void OnGenerated(ChunkKey key, int idx)
    {
        if (_store[idx].PendingNeighbors == 0 && !_store[idx].Meshed) _toMesh.Enqueue(key);

        foreach (var off in NeighborOffsets)
        {
            var nk = key.Plus(off);
            if (!WorldLimits.InY(nk.Y)) continue;
            if (!_store.TryGetIndex(nk, out int ni)) continue;

            ref var n = ref _store[ni];
            if (n.PendingNeighbors > 0 && --n.PendingNeighbors == 0 && n.Generated && !n.Meshed)
                _toMesh.Enqueue(nk);
        }
    }

    private void MeshStep(ChunkPriority prio)
    {
        int total = _toMesh.TakeBest(prio, _meshSel.Length, _meshValid, _meshSel);
        long deadline = Deadline(_settings.MeshBudgetMs);
        int pos = 0;
        while (pos < total)
        {
            int n = Math.Min(MeshBatch, total - pos);

            // подготовка в главном потоке: соседи и флаг «запланирован»
            int m = 0;
            for (int i = 0; i < n; i++)
            {
                var key = _meshSel[pos + i];
                _store.TryGetIndex(key, out int idx);
                ref var c = ref _store[idx];
                if (c.Meshed) continue;          // дубликат
                c.Meshed = true;

                _meshWorkers[m].Sampler.Load(_store, key);
                _meshJobs[m] = new MeshJob { Key = key, Voxels = c.Voxels, Slot = m };
                m++;
            }

            _cpu.Run(_meshJobs, m, _meshFn);
            for (int i = 0; i < m; i++) ApplyMesh(i);

            pos += n;
            if (Stopwatch.GetTimestamp() >= deadline) break;
        }
        for (int i = pos; i < total; i++) _toMesh.Enqueue(_meshSel[i]);
    }

    // выполняется в пуле: читает пул и свой слот, ничего общего не пишет
    private void MeshOne(ref MeshJob job)
    {
        var w = _meshWorkers[job.Slot];
        job.VertexCount = job.IndexCount = 0;
        if (!_pool.IsAlive(job.Voxels)) return;             // воздух

        var voxels = _pool.GetSpan(job.Voxels);
        if (w.IsBuried(voxels)) return;

        w.Mesher.Mesh(voxels);
        job.VertexCount = w.Mesher.VertexCount;
        job.IndexCount = w.Mesher.IndexCount;
    }

    private void ApplyMesh(int i)
    {
        ref var job = ref _meshJobs[i];
        _store.TryGetIndex(job.Key, out int idx);
        ref var c = ref _store[idx];

        // CPU-меш, который не успел загрузиться, заменяется новым
        _pool.FreeIfAlive(ref c.MeshVertices);
        _pool.FreeIfAlive(ref c.MeshIndices);
        c.MeshVertexCount = c.MeshIndexCount = 0;

        if (job.IndexCount == 0)
        {
            // блок стал пустым: старый меш на GPU убираем
            if (c.Gpu.IndexCount > 0)
            {
                _sink.Release(c.Gpu);
                c.Gpu = default;
            }
            return;
        }

        var w = _meshWorkers[job.Slot];
        c.MeshVertices = _pool.AllocateFrom<VoxelVertex>(w.Mesher.Vertices.AsSpan(0, job.VertexCount));
        c.MeshIndices = _pool.AllocateFrom<ushort>(w.Mesher.Indices.AsSpan(0, job.IndexCount));
        c.MeshVertexCount = job.VertexCount;
        c.MeshIndexCount = job.IndexCount;
        _toUpload.Enqueue(job.Key);
    }

    private void UploadStep(ChunkPriority prio)
    {
        int total = _toUpload.TakeBest(prio, _uploadSel.Length, _uploadValid, _uploadSel);
        long deadline = Deadline(_settings.UploadBudgetMs);
        int pos = 0;
        for (; pos < total; pos++)
        {
            if (pos > 0 && Stopwatch.GetTimestamp() >= deadline) break;

            _store.TryGetIndex(_uploadSel[pos], out int idx);
            ref var c = ref _store[idx];
            bool ok = _sink.TryUpload(_pool.GetBytes(c.MeshVertices), c.MeshVertexCount,
                                      _pool.GetBytes(c.MeshIndices), c.MeshIndexCount, out GpuRange range);
            if (!ok) break;                                  // арена полна — остальное в следующем кадре

            if (c.Gpu.IndexCount > 0) _sink.Release(c.Gpu);   // старый меш заменяем после загрузки нового
            c.Gpu = range;
            _pool.FreeIfAlive(ref c.MeshVertices);            // CPU-копия больше не нужна
            _pool.FreeIfAlive(ref c.MeshIndices);
            c.MeshVertexCount = c.MeshIndexCount = 0;
        }
        for (int i = pos; i < total; i++) _toUpload.Enqueue(_uploadSel[i]);
    }

    // ───────────── валидаторы очередей ─────────────

    private bool InRadius(ChunkKey k)
    {
        long dx = k.X - _cx, dz = k.Z - _cz;
        return dx * dx + dz * dz <= (long)_radius * _radius;
    }

    private bool IsGenPending(ChunkKey k) => _store.TryGetIndex(k, out int i) && !_store[i].Generated;

    private bool IsMeshReady(ChunkKey k) =>
        _store.TryGetIndex(k, out int i)
        && _store[i].Generated && !_store[i].Meshed && _store[i].PendingNeighbors == 0
        && InRadius(k);

    private bool IsUploadPending(ChunkKey k) =>
        _store.TryGetIndex(k, out int i) && _store[i].MeshIndexCount > 0;

    // ───────────── утилиты ─────────────

    private static long Deadline(float ms) => Stopwatch.GetTimestamp() + (long)(ms * Stopwatch.Frequency / 1000.0);

    private static ChunkKey[] BuildNeighborOffsets()
    {
        var list = new List<ChunkKey>(26);
        for (int dz = -1; dz <= 1; dz++)
        for (int dy = -1; dy <= 1; dy++)
        for (int dx = -1; dx <= 1; dx++)
            if ((dx | dy | dz) != 0) list.Add(new ChunkKey(dx, dy, dz));
        return list.ToArray();
    }

    // ───────────── редактирование блоков ─────────────

    private readonly ushort[] _emptyChunk = new ushort[Vox.Volume];

    /// <summary>Блок по мировым координатам вокселя. false — чанк ещё не сгенерирован или вне мира.</summary>
    public bool TryGetBlock(int wx, int wy, int wz, out ushort block)
    {
        block = 0;
        var key = ToChunk(wx, wy, wz, out int lx, out int ly, out int lz);
        if (!WorldLimits.InY(key.Y)) return false;
        if (!_store.TryGetIndex(key, out int idx)) return _air.Contains(key);   // воздушный чанк: известен, но не хранится
        if (!_store[idx].Generated) return false;
        if (_pool.IsAlive(_store[idx].Voxels))
            block = _pool.GetSpan(_store[idx].Voxels)[Vox.Index(lx, ly, lz)];
        return true;
    }

    /// <summary>
    /// Меняет блок и ставит на перестройку меш затронутых чанков. Воздушный чанк, в который поставили блок,
    /// создаётся заново и появляется без анимации; опустевший чанк становится воздухом.
    /// Возвращает false, если чанк не готов или блок не изменился.
    /// </summary>
    public bool SetBlock(int wx, int wy, int wz, ushort block)
    {
        var key = ToChunk(wx, wy, wz, out int lx, out int ly, out int lz);
        if (!WorldLimits.InY(key.Y)) return false;

        if (!_store.TryGetIndex(key, out int idx))
        {
            if (block == 0 || !_air.Contains(key)) return false;     // воздух на воздухе, либо чанк ещё не сгенерирован
            _air.Remove(key);
            idx = _store.Add(key, CountPending(key));
            _store[idx].Generated = true;
            _store[idx].Voxels = _pool.AllocateFrom<ushort>(_emptyChunk.AsSpan());
        }
        else if (!_store[idx].Generated) return false;

        if (!_pool.IsAlive(_store[idx].Voxels))
        {
            if (block == 0) return false;                             // воздух и так воздух
            _store[idx].Voxels = _pool.AllocateFrom<ushort>(_emptyChunk.AsSpan());
        }

        var voxels = _pool.GetSpan(_store[idx].Voxels);
        int li = Vox.Index(lx, ly, lz);
        if (voxels[li] == block) return false;
        voxels[li] = block;

        MarkDirty(key, lx, ly, lz);

        // опустел — становится воздухом (соседи по-прежнему считают его готовым)
        if (!HasSolid(voxels) && _store.TryGetIndex(key, out int emptyIdx))
            BecomeAir(key, emptyIdx);
        return true;
    }

    /// <summary>Чанк и соседи по тем осям, где вксель стоит на краю (включая углы: там тоже меняется AO).</summary>
    private void MarkDirty(ChunkKey key, int lx, int ly, int lz)
    {
        foreach (int dx in EdgeOffsets(lx))
        foreach (int dy in EdgeOffsets(ly))
        foreach (int dz in EdgeOffsets(lz))
        {
            var nk = new ChunkKey(key.X + dx, key.Y + dy, key.Z + dz);
            if (!WorldLimits.InY(nk.Y) || !_store.TryGetIndex(nk, out int ni) || !_store[ni].Generated) continue;

            // старый меш остаётся на экране, пока не загрузится новый
            // чанк, который впервые показывается из-за правки игрока, появляется без анимации
            if (dx == 0 && dy == 0 && dz == 0 && !_store[ni].Shown)
            {
                _store[ni].Shown = true;
                _store[ni].FadeStart = NoFade;
            }
            _store[ni].Meshed = false;
            _toMesh.Enqueue(nk);
        }
    }

    private static int[] EdgeOffsets(int local) =>
        local == 0 ? new[] { 0, -1 } : local == Vox.Size - 1 ? new[] { 0, 1 } : new[] { 0 };

    /// <summary>Готов ли чанк для соседей: сгенерирован и хранится, либо известен как воздух.</summary>
    private bool IsReady(ChunkKey k) =>
        (_store.TryGetIndex(k, out int i) && _store[i].Generated) || _air.Contains(k);

    /// <summary>Сколько соседей чанка ещё не готовы (только внутри мира по Y).</summary>
    private int CountPending(ChunkKey key)
    {
        int pending = 0;
        foreach (var off in NeighborOffsets)
        {
            var nk = key.Plus(off);
            if (WorldLimits.InY(nk.Y) && !IsReady(nk)) pending++;
        }
        return pending;
    }

    /// <summary>Соседи сгенерированного чанка: уменьшаем счётчики, готовые кладём в меш.</summary>
    private void DecrementNeighbors(ChunkKey key)
    {
        foreach (var off in NeighborOffsets)
        {
            var nk = key.Plus(off);
            if (!WorldLimits.InY(nk.Y)) continue;
            if (!_store.TryGetIndex(nk, out int ni)) continue;

            ref var n = ref _store[ni];
            if (n.PendingNeighbors > 0 && --n.PendingNeighbors == 0 && n.Generated && !n.Meshed)
                _toMesh.Enqueue(nk);
        }
    }

    /// <summary>Чанк опустел: освобождаем его память и выносим из хранилища. Соседи продолжают считать его готовым.</summary>
    private void BecomeAir(ChunkKey key, int idx)
    {
        Chunk c = _store.RemoveAt(idx);
        if (c.Gpu.IndexCount > 0) _sink.Release(c.Gpu);
        _pool.FreeIfAlive(ref c.Voxels);
        _pool.FreeIfAlive(ref c.MeshVertices);
        _pool.FreeIfAlive(ref c.MeshIndices);
        _air.Add(key);
    }

    private static bool HasSolid(ReadOnlySpan<ushort> voxels)
    {
        for (int i = 0; i < voxels.Length; i++)
            if (voxels[i] != 0) return true;
        return false;
    }

    private static ChunkKey ToChunk(int wx, int wy, int wz, out int lx, out int ly, out int lz)
    {
        int cx = FloorDiv(wx, Vox.Size), cy = FloorDiv(wy, Vox.Size), cz = FloorDiv(wz, Vox.Size);
        lx = wx - cx * Vox.Size;
        ly = wy - cy * Vox.Size;
        lz = wz - cz * Vox.Size;
        return new ChunkKey(cx, cy, cz);
    }

    private static int FloorDiv(int a, int b)
    {
        int q = a / b;
        if (a % b != 0 && (a < 0) != (b < 0)) q--;
        return q;
    }

    public void Dispose() => _gen.Dispose();
}
