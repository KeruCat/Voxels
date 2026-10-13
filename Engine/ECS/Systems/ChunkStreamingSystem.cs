using Engine.ECS.Components;
using Engine.Graphics.Memory;
using Engine.Memory;
using Engine.Voxels;
using Friflo.Engine.ECS;
using Friflo.Engine.ECS.Systems;

namespace Engine.ECS.Systems;

/// <summary>
/// Бесконечный мир: вокруг игрока держит «диск» чанков. Ближние создаёт (сначала самые близкие),
/// дальние выгружает вместе с пулом и GPU-ареной. Радиус выгрузки больше радиуса подгрузки (гистерезис),
/// чтобы граница не дрожала, а у мешера всегда были соседи.
///
/// Очередь «что создать» строится ОДИН раз при смене центра/радиуса (диск смещений отсортирован заранее),
/// а не пересчитывается каждый кадр. Выгрузка — одним проходом: удаление сущностей идёт через CommandBuffer.
/// </summary>
internal sealed class ChunkStreamingSystem : BaseSystem
{
    private readonly EntityStore _world;
    private readonly ChunkMap _map;
    private readonly MemoryPool _pool;
    private readonly GpuArena _vtx, _idx;
    private readonly WorldSettings _settings;
    private readonly WorldStats _stats;
    private readonly PipelineState _state;
    private readonly ArchetypeQuery<WorldPosition> _players;
    private readonly ArchetypeQuery<ChunkCoord> _chunks;
    private readonly CommandBuffer _ecb;
    private readonly List<(int X, int Y, int Z)> _queue = new();

    private (int Dx, int Dz, int D2)[] _disc = [];
    private int _discFor = -1;
    private int _cursor;
    private int _cx, _cz, _appliedDistance;
    private bool _hasCenter;

    public ChunkStreamingSystem(EntityStore world, ChunkMap map, MemoryPool pool, GpuArena vtx, GpuArena idx,
                                WorldSettings settings, WorldStats stats, PipelineState state)
    {
        (_world, _map, _pool, _vtx, _idx, _settings, _stats, _state) = (world, map, pool, vtx, idx, settings, stats, state);
        _players = world.Query<WorldPosition>();
        _chunks = world.Query<ChunkCoord>();
        _ecb = world.GetCommandBuffer();
        _ecb.ReuseBuffer = true;
    }

    protected override void OnUpdateGroup()
    {
        if (!TryGetPlayerChunk(out int pcx, out int pcz)) return;

        int rd = _settings.RenderDistance;
        if (!_hasCenter || pcx != _cx || pcz != _cz || rd != _appliedDistance)
        {
            (_cx, _cz, _appliedDistance, _hasCenter) = (pcx, pcz, rd, true);
            // мешер берёт отсюда область, которую нужно показывать; смена центра меняет набор подходящих чанков
            (_state.CenterX, _state.CenterZ, _state.Radius) = (pcx, pcz, rd);
            _state.Version++;
            Unload(rd);
            BuildQueue(rd);
        }
        SpawnQueued();

        _stats.Chunks = _map.Count;
    }

    private bool TryGetPlayerChunk(out int cx, out int cz)
    {
        foreach (var e in _players.Entities)
        {
            ref readonly var p = ref e.GetComponent<WorldPosition>();
            cx = (int)Math.Floor(p.X / Chunk.Size);
            cz = (int)Math.Floor(p.Z / Chunk.Size);
            return true;
        }
        cx = cz = 0;
        return false;
    }

    // ───────────── подгрузка ─────────────

    private void BuildDisc(int rd)
    {
        double genRadius = rd + 1.5;                        // на +1.5 дальше видимого: мешеру нужны соседи
        int genR2 = (int)(genRadius * genRadius);
        int reach = rd + 2;

        var list = new List<(int Dx, int Dz, int D2)>();
        for (int dz = -reach; dz <= reach; dz++)
        for (int dx = -reach; dx <= reach; dx++)
        {
            int d2 = dx * dx + dz * dz;
            if (d2 <= genR2) list.Add((dx, dz, d2));
        }
        list.Sort(static (a, b) => a.D2.CompareTo(b.D2));   // ближние первыми
        _disc = list.ToArray();
        _discFor = rd;
    }

    private void BuildQueue(int rd)
    {
        if (_discFor != rd) BuildDisc(rd);

        _queue.Clear();
        _cursor = 0;
        foreach (var (dx, dz, _) in _disc)
            for (int cy = WorldLimits.MinChunkY; cy <= WorldLimits.MaxChunkY; cy++)   // колонка идёт подряд — удобно генератору
                if (!_map.TryGet(_cx + dx, cy, _cz + dz, out _))
                    _queue.Add((_cx + dx, cy, _cz + dz));
    }

    private void SpawnQueued()
    {
        int n = Math.Min(_settings.SpawnPerFrame, _queue.Count - _cursor);
        while (n-- > 0)
        {
            var (x, y, z) = _queue[_cursor++];
            if (!_map.TryGet(x, y, z, out _))
                ChunkSpawner.Spawn(_world, _map, x, y, z);
        }
    }

    // ───────────── выгрузка ─────────────

    private void Unload(int rd)
    {
        int unloadR2 = (rd + 3) * (rd + 3);
        int removed = 0;

        foreach (var (coords, entities) in _chunks.Chunks)
        {
            var cs = coords.Span;
            for (int i = 0; i < entities.Length; i++)
            {
                int dx = cs[i].X - _cx, dz = cs[i].Z - _cz;
                if (dx * dx + dz * dz <= unloadR2) continue;

                Release(entities.EntityAt(i));          // ресурсы освобождаем сразу, сущность удалится в Playback
                removed++;
            }
        }

        // Удаление применяем сразу, а не в конце группы: генерация/меширование этого же кадра
        // не должны видеть уже выгруженные чанки (иначе они выделят память в пуле для мёртвых сущностей).
        _ecb.Playback();
        if (removed > 0) _state.Version++;
    }

    private void Release(Entity e)
    {
        ref var c = ref e.GetComponent<ChunkCoord>();
        _map.Remove(c.X, c.Y, c.Z);

        var data = e.GetComponent<ChunkData>().Data.Handle;
        if (_pool.IsAlive(data)) _pool.Free(data);

        ref var mesh = ref e.GetComponent<Mesh>();
        _pool.FreeIfAlive(ref mesh.Vertices);
        _pool.FreeIfAlive(ref mesh.Indices);

        ref var gpu = ref e.GetComponent<GpuMesh>();
        if (gpu.IndexCount > 0)
        {
            _vtx.Free(gpu.VertexOffset, gpu.VertexCount);
            _idx.Free(gpu.IndexOffset, gpu.IndexCount);
        }
        _ecb.DeleteEntity(e.Id);
    }
}
