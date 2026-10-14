using Engine.Memory;
using Game.Voxels.Meshing;

namespace Game.World;

/// <summary>Слот меширования: собственный мешер и сэмплер соседей, поэтому задачи в пуле ничего не делят.</summary>
internal sealed class MeshWorker
{
    private static readonly (int X, int Y, int Z)[] FaceNeighbors =
        [(-1, 0, 0), (1, 0, 0), (0, -1, 0), (0, 1, 0), (0, 0, -1), (0, 0, 1)];

    public readonly BinaryGreedyMesher Mesher = new();
    public readonly NeighborSampler Sampler;
    private readonly MemoryPool _pool;

    public MeshWorker(MemoryPool pool)
    {
        _pool = pool;
        Sampler = new NeighborSampler(pool);
        Mesher.OutsideSampler = Sampler.Sample;
    }

    /// <summary>Чанк целиком твёрдый и все 6 соседей тоже ⇒ видимых граней нет.</summary>
    public bool IsBuried(ReadOnlySpan<ushort> voxels)
    {
        if (voxels.Contains((ushort)0)) return false;
        foreach (var (dx, dy, dz) in FaceNeighbors)
        {
            var h = Sampler.Neighbor(dx, dy, dz);
            if (!_pool.IsAlive(h) || _pool.GetSpan(h).Contains((ushort)0)) return false;
        }
        return true;
    }
}
