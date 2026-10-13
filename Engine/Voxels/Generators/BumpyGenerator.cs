namespace Engine.Voxels.Generators;

/// <summary>«Кривой» ландшафт: холмы со случайными выбоинами — для визуальной проверки мешера.</summary>
public sealed class BumpyGenerator : IChunkGenerator
{
    private readonly ushort _block;
    private readonly int _seed;

    public BumpyGenerator(ushort blockId = 1, int seed = 12345)
    {
        _block = blockId;
        _seed = seed;
    }

    public bool Fill(Span<ushort> voxels, int cx, int cy, int cz)
    {
        const int size = Chunk.Size;

        int Height(int x, int z)
        {
            int hash = Hash(x + cx * size, z + cz * size);
            int h = 8
                + (int)(3.0 * Math.Sin((x + cx * size) * 0.5) * Math.Cos((z + cz * size) * 0.45))
                + (hash & 3);
            return Math.Clamp(h, 1, size - 1);
        }

        bool any = false;
        for (int z = 0; z < size; z++)
            for (int x = 0; x < size; x++)
            {
                int h = Height(x, z);
                for (int y = 0; y < size; y++)
                {
                    int wy = y + cy * size;
                    ushort v = 0;
                    if (wy < h)
                    {
                        v = _block;
                        // редкие выбоины, чтобы было видно «дырявые» участки
                        if (wy == h - 1 && (Hash(x + cx * size * 7, z + cz * size * 13) % 11) == 0)
                            v = 0;
                    }
                    voxels[Chunk.Index(x, y, z)] = v;
                    any |= v != 0;
                }
            }
        return any;
    }

    private int Hash(int x, int z)
    {
        unchecked
        {
            uint h = (uint)_seed;
            h = h * 16777619u ^ (uint)x;
            h = h * 16777619u ^ (uint)z;
            h ^= h >> 15;
            h *= 2246822519u;
            h ^= h >> 13;
            return (int)(h & 0x7FFFFFFF);
        }
    }
}
