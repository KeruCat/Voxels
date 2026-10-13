using System.Diagnostics;
using Engine.Core;
using Engine.ECS.Components;
using Engine.ECS.Markers;
using Engine.Memory;
using Engine.Voxels;
using Engine.Voxels.Generators;
using Friflo.Engine.ECS;
using Friflo.Engine.ECS.Systems;

namespace Engine.ECS.Systems;

/// <summary>
/// Генерирует воксели чанков без Generated. За кадр:
///  1) один проход по запросу отбирает лучших кандидатов (ближе к игроку и по взгляду) — без сортировки всего;
///  2) пачками по BatchSize параллельно заполняет воксели (у каждого потока свой генератор);
///  3) в главном потоке кладёт данные в пул, а изменения компонентов/тегов откладывает в CommandBuffer.
/// Работает, пока не выйдет бюджет времени <see cref="WorldSettings.GenBudgetMs"/>.
/// </summary>
internal sealed class ChunkGenerationSystem : QuerySystem<ChunkCoord, ChunkData>
{
    private const int Candidates = 512;     // сколько лучших кандидатов отбираем за кадр
    private static readonly int BatchSize = Math.Clamp(Environment.ProcessorCount * 4, 16, 64);

    private struct Cand { public int Id, X, Y, Z; }

    private static readonly Comparison<Cand> ColumnOrder = static (a, b) =>
    {
        int c = a.X.CompareTo(b.X);
        if (c != 0) return c;
        c = a.Z.CompareTo(b.Z);
        return c != 0 ? c : a.Y.CompareTo(b.Y);
    };

    private readonly MemoryPool _pool;
    private readonly FloatingOrigin _origin;
    private readonly WorldSettings _settings;
    private readonly PipelineState _state;
    private readonly ThreadLocal<IChunkGenerator> _gen;
    private readonly Tags _doneTags;

    private readonly ushort[][] _bufs = new ushort[BatchSize][];
    private readonly bool[] _any = new bool[BatchSize];
    private readonly Cand[] _picked = new Cand[Candidates];
    private readonly PriorityQueue<Cand, float> _heap = new();
    private readonly ParallelOptions _par = new() { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1) };
    private readonly Action<int> _work;
    private int _pickedCount, _batchStart;

    public ChunkGenerationSystem(MemoryPool pool, Func<IChunkGenerator> generatorFactory,
                                 FloatingOrigin origin, WorldSettings settings, PipelineState state)
    {
        (_pool, _origin, _settings, _state) = (pool, origin, settings, state);
        _gen = new ThreadLocal<IChunkGenerator>(generatorFactory);   // генератор TerrainGenerator не потокобезопасен
        _doneTags = Tags.Get<Generated, Dirty>();
        _work = GenerateOne;                                         // делегат создаётся один раз
        Filter.WithoutAllTags(Tags.Get<Generated>());
    }

    protected override void OnUpdate()
    {
        SelectBest();
        if (_pickedCount == 0) return;

        var ecb = CommandBuffer;
        long deadline = Stopwatch.GetTimestamp() + (long)(_settings.GenBudgetMs * Stopwatch.Frequency / 1000.0);
        int done = 0;

        for (int start = 0; start < _pickedCount; start += BatchSize)
        {
            int n = Math.Min(BatchSize, _pickedCount - start);

            // внутри пачки группируем по колонкам: у генератора есть кэш высот колонки (X,Z)
            _picked.AsSpan(start, n).Sort(ColumnOrder);
            _batchStart = start;

            if (n == 1) GenerateOne(0);
            else Parallel.For(0, n, _par, _work);

            // пул не потокобезопасен — выделяем в главном потоке
            for (int i = 0; i < n; i++)
            {
                int id = _picked[start + i].Id;
                var data = _any[i] ? _pool.AllocateFrom<ushort>(_bufs[i]) : default;
                ecb.AddComponent(id, new ChunkData { Data = data });
                ecb.AddTags(id, _doneTags);
            }
            done += n;
            if (Stopwatch.GetTimestamp() >= deadline) break;
        }

        if (done > 0) _state.Version++;
    }

    private void GenerateOne(int i)
    {
        ref readonly var c = ref _picked[_batchStart + i];
        var buf = _bufs[i] ??= new ushort[Chunk.Volume];
        _any[i] = _gen.Value!.Fill(buf, c.X, c.Y, c.Z);
    }

    /// <summary>Оставляет в _picked до Candidates лучших (по возрастанию приоритета: лучший — первый).</summary>
    private void SelectBest()
    {
        _heap.Clear();
        _pickedCount = 0;

        var prio = new ChunkPriority(_origin);   // StreamingFocus локальный → считаем от floating origin

        foreach (var (coords, _, entities) in Query.Chunks)
        {
            var cs = coords.Span;
            for (int i = 0; i < entities.Length; i++)
            {
                ref readonly var c = ref cs[i];
                float p = prio.Of(c.X, c.Y, c.Z);          // меньше = раньше
                if (_heap.Count >= Candidates)
                {
                    _heap.TryPeek(out _, out float negWorst);
                    if (p >= -negWorst) continue;          // хуже худшего из отобранных
                }

                var cand = new Cand { Id = entities[i], X = c.X, Y = c.Y, Z = c.Z };
                // куча хранит Candidates лучших; корень = худший из них (приоритет -p минимален)
                if (_heap.Count < Candidates) _heap.Enqueue(cand, -p);
                else _heap.EnqueueDequeue(cand, -p);
            }
        }

        int count = _heap.Count;
        _pickedCount = count;
        for (int k = count - 1; k >= 0; k--) _picked[k] = _heap.Dequeue();   // худший вышел первым → кладём с конца
    }
}
