using Engine.Memory;
using Game.Voxels;

namespace Game.World;

/// <summary>
/// 3×3×3 окрестность чанка для меширования. Load вызывается ТОЛЬКО в главном потоке,
/// когда все соседи уже сгенерированы (PendingNeighbors == 0). Sample читает из пула и безопасен в параллели.
/// За пределами мира по Y — воздух.
/// </summary>
internal sealed class NeighborSampler
{
    private readonly MemoryPool _pool;
    private readonly PoolHandle<ushort>[] _n = new PoolHandle<ushort>[27];

    public NeighborSampler(MemoryPool pool) => _pool = pool;

    private static int Slot(int dx, int dy, int dz) => (dx + 1) + (dy + 1) * 3 + (dz + 1) * 9;

    public void Load(ChunkStore store, ChunkKey key)
    {
        for (int dz = -1; dz <= 1; dz++)
        for (int dy = -1; dy <= 1; dy++)
        for (int dx = -1; dx <= 1; dx++)
        {
            int i = Slot(dx, dy, dz);
            _n[i] = default;
            if ((dx | dy | dz) == 0) continue;

            var nk = key.Plus(new ChunkKey(dx, dy, dz));
            if (!WorldLimits.InY(nk.Y)) continue;
            if (store.TryGetIndex(nk, out int ni)) _n[i] = store[ni].Voxels;
        }
    }

    public PoolHandle<ushort> Neighbor(int dx, int dy, int dz) => _n[Slot(dx, dy, dz)];

    /// <summary>Координаты в системе ЭТОГО чанка, вне 0..15 (не дальше чем на 1 чанк в каждую сторону).</summary>
    public ushort Sample(int x, int y, int z)
    {
        int dx = x < 0 ? -1 : x >= Chunk.Size ? 1 : 0;
        int dy = y < 0 ? -1 : y >= Chunk.Size ? 1 : 0;
        int dz = z < 0 ? -1 : z >= Chunk.Size ? 1 : 0;
        var h = _n[Slot(dx, dy, dz)];
        if (!_pool.IsAlive(h)) return 0;
        return _pool.GetSpan(h)[Vox.Index(x - dx * Chunk.Size, y - dy * Chunk.Size, z - dz * Chunk.Size)];
    }
}
