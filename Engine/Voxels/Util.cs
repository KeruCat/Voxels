using Engine.ECS.Components;
using Engine.Memory;
using Friflo.Engine.ECS;

namespace Engine.Voxels;

internal static class PoolExt
{
    public static void FreeIfAlive(this MemoryPool pool, ref PoolHandle h)
    {
        if (pool.IsAlive(h)) pool.Free(h);
        h = default;
    }

    public static void FreeIfAlive<T>(this MemoryPool pool, ref PoolHandle<T> h) where T : unmanaged
    {
        if (pool.IsAlive(h.Handle)) pool.Free(h.Handle);
        h = default;
    }
}

/// <summary>
/// «Версия топологии» мира: растёт, когда чанки сгенерировались или выгрузились.
/// Мешеру это сигнал, что у заблокированных (ждущих соседей) чанков что-то могло измениться;
/// пока версия не менялась, пересматривать их бессмысленно.
/// </summary>
internal sealed class PipelineState
{
    public int Version;
    public int CenterX, CenterZ, Radius;   // выставляет стриминг: чанк-колонка игрока и радиус показа
}

// чанк-координата → сущность; нужна для соседей и для выгрузки
internal sealed class ChunkMap
{
    private readonly Dictionary<long, Entity> _map = new();
    public int Count => _map.Count;

    // X/Z — по 21 биту, Y — 10 бит (хватает с запасом: ±1 млн чанков по горизонтали)
    private static long Key(int x, int y, int z)
        => (x & 0x1FFFFFL) | ((z & 0x1FFFFFL) << 21) | ((long)(y & 0x3FF) << 42);

    public void Add(int x, int y, int z, Entity e) => _map[Key(x, y, z)] = e;
    public bool TryGet(int x, int y, int z, out Entity e) => _map.TryGetValue(Key(x, y, z), out e);
    public void Remove(int x, int y, int z) => _map.Remove(Key(x, y, z));
}

internal static class ChunkSpawner
{
    public static Entity Spawn(EntityStore world, ChunkMap map, int x, int y, int z)
    {
        var e = world.CreateEntity(new ChunkCoord { X = x, Y = y, Z = z },
                                   new ChunkData(), new Mesh(), new GpuMesh());
        map.Add(x, y, z, e);
        return e;
    }
}
