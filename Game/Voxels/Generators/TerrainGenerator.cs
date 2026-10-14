using Game.World;
namespace Game.Voxels.Generators;
public sealed class TerrainGenerator : IChunkGenerator
{
    public const ushort Grass = 1, Dirt = 2, Stone = 3, Sand = 4, Water = 5, Snow = 6;
    public const int SeaLevel = 0;
    private const int SnowLine = 80;
    private const int N = Vox.Size;

    private readonly int _seed;
    private readonly int[] _height = new int[N * N];
    private readonly ushort[] _top = new ushort[N * N];
    private const int G = N / 2 + 1;                     
    private readonly double[] _grid = new double[G * G];
    private int _cacheX = int.MinValue, _cacheZ = int.MinValue, _minH, _maxH;

    public TerrainGenerator(int seed = 1337) => _seed = seed;

    public bool Fill(Span<ushort> voxels, int cx, int cy, int cz)
    {
        PrepareColumn(cx, cz);

        int y0 = cy * N, y1 = y0 + N;
        if (y0 >= Math.Max(_maxH, SeaLevel)) return false;                                
        if (y1 <= _minH - 4)                                                                
        {
            voxels.Slice(0, Vox.Volume).Fill(Stone);
            return true;
        }

        for (int z = 0; z < N; z++)
        for (int x = 0; x < N; x++)
        {
            int col = x + z * N;
            int h = _height[col];
            ushort top = _top[col];
            ushort soil = top == Sand ? Sand : Dirt;

            for (int y = 0; y < N; y++)
            {
                int wy = y0 + y;
                ushort v;
                if (wy >= h) v = wy < SeaLevel ? Water : (ushort)0;
                else if (wy < h - 4) v = Stone;
                else if (wy == h - 1) v = top;
                else v = soil;
                voxels[Vox.Index(x, y, z)] = v;
            }
        }
        return true;
    }

    private void PrepareColumn(int cx, int cz)
    {
        if (cx == _cacheX && cz == _cacheZ) return;
        _cacheX = cx; _cacheZ = cz;
        _minH = int.MaxValue; _maxH = int.MinValue;
        for (int gz = 0; gz < G; gz++)
        for (int gx = 0; gx < G; gx++)
            _grid[gx + gz * G] = RawHeight((long)cx * N + gx * 2, (long)cz * N + gz * 2);
        for (int z = 0; z < N; z++)
        for (int x = 0; x < N; x++)
        {
            int gx = x >> 1, gz = z >> 1;
            double tx = (x & 1) * 0.5, tz = (z & 1) * 0.5;      
            double a = _grid[gx + gz * G],       b = _grid[gx + 1 + gz * G];
            double c = _grid[gx + (gz + 1) * G], d = _grid[gx + 1 + (gz + 1) * G];
            double hRaw = (a + (b - a) * tx) * (1 - tz) + (c + (d - c) * tx) * tz;

            int h = ToHeight(hRaw);
            int col = x + z * N;
            _height[col] = h;
            _top[col] = h >= SnowLine ? Snow : h <= SeaLevel + 2 ? Sand : Grass;
            if (h < _minH) _minH = h;
            if (h > _maxH) _maxH = h;
        }
    }
    private double RawHeight(double x, double z)           
    {
        double continent = Noise.Fbm(_seed, x * 0.0020, z * 0.0020, 4);
        double hills = Noise.Fbm(_seed + 101, x * 0.0100, z * 0.0100, 5);
        double ridge = 1.0 - Math.Abs(Noise.Fbm(_seed + 202, x * 0.0040, z * 0.0040, 4));
        ridge *= ridge;
        double mountains = Math.Clamp((continent - 0.1) * 2.5, 0.0, 1.0);
        return 14 + continent * 36 + hills * 12 + ridge * mountains * 60;
    }

    private static int ToHeight(double h) =>
        (int)Math.Clamp(Math.Floor(h), WorldLimits.MinHeight, WorldLimits.MaxHeight);

    public int SurfaceHeight(long wx, long wz) => ToHeight(RawHeight(wx, wz));
}
internal static class Noise
{
    private static readonly double[] Gx = [1, 0.70710678, 0, -0.70710678, -1, -0.70710678, 0, 0.70710678];
    private static readonly double[] Gz = [0, 0.70710678, 1, 0.70710678, 0, -0.70710678, -1, -0.70710678];

    public static double Fbm(int seed, double x, double z, int octaves)
    {
        double sum = 0, amp = 1, norm = 0, freq = 1;
        for (int i = 0; i < octaves; i++)
        {
            sum += amp * Perlin(seed + i * 1013, x * freq, z * freq);
            norm += amp;
            amp *= 0.5;
            freq *= 2.0;
        }
        return sum / norm;
    }

    private static double Perlin(int seed, double x, double z)
    {
        double fx = Math.Floor(x), fz = Math.Floor(z);
        int x0 = (int)fx, z0 = (int)fz;
        double dx = x - fx, dz = z - fz;
        double u = Fade(dx), v = Fade(dz);

        double a = Dot(Hash(seed, x0, z0), dx, dz);
        double b = Dot(Hash(seed, x0 + 1, z0), dx - 1, dz);
        double c = Dot(Hash(seed, x0, z0 + 1), dx, dz - 1);
        double d = Dot(Hash(seed, x0 + 1, z0 + 1), dx - 1, dz - 1);

        return Lerp(Lerp(a, b, u), Lerp(c, d, u), v) * 1.4142;   // ≈ -1..1
    }

    private static double Dot(int hash, double dx, double dz) => Gx[hash & 7] * dx + Gz[hash & 7] * dz;
    private static double Fade(double t) => t * t * t * (t * (t * 6 - 15) + 10);
    private static double Lerp(double a, double b, double t) => a + (b - a) * t;

    private static int Hash(int seed, int x, int z)
    {
        unchecked
        {
            uint h = (uint)seed;
            h = h * 16777619u ^ (uint)x;
            h = h * 16777619u ^ (uint)z;
            h ^= h >> 15;
            h *= 2246822519u;
            h ^= h >> 13;
            h *= 3266489917u;
            h ^= h >> 16;
            return (int)(h & 0x7FFFFFFF);
        }
    }
}
