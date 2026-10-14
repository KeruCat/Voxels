using System.Numerics;

namespace Game.World;

/// <summary>Попадание луча в блок: сам блок и нормаль грани, в которую вошёл луч (компоненты −1, 0 или +1).</summary>
public readonly record struct BlockHit(int X, int Y, int Z, int NX, int NY, int NZ, ushort Block);

public static class BlockRaycast
{
    /// <summary>
    /// Луч по сетке вокселей (алгоритм Амантидеса–Во). Возвращает первый твёрдый блок.
    /// Если луч упирается в ещё не загруженный чанк, поиск останавливается без попадания.
    /// </summary>
    public static bool Cast(VoxelWorld world, double ox, double oy, double oz, Vector3 dir, double maxDist, out BlockHit hit)
    {
        hit = default;
        double dx = dir.X, dy = dir.Y, dz = dir.Z;

        int x = (int)Math.Floor(ox), y = (int)Math.Floor(oy), z = (int)Math.Floor(oz);
        int sx = dx > 0 ? 1 : dx < 0 ? -1 : 0;
        int sy = dy > 0 ? 1 : dy < 0 ? -1 : 0;
        int sz = dz > 0 ? 1 : dz < 0 ? -1 : 0;

        double tMaxX = AxisStart(ox, x, sx, dx, out double tDeltaX);
        double tMaxY = AxisStart(oy, y, sy, dy, out double tDeltaY);
        double tMaxZ = AxisStart(oz, z, sz, dz, out double tDeltaZ);

        if (world.TryGetBlock(x, y, z, out ushort start))
        {
            if (start != 0)
            {
                hit = new BlockHit(x, y, z, 0, 0, 0, start);   // стоим внутри блока
                return true;
            }
        }
        else return false;

        int nx = 0, ny = 0, nz = 0;
        double t = 0;
        while (true)
        {
            if (tMaxX < tMaxY && tMaxX < tMaxZ)
            {
                t = tMaxX; tMaxX += tDeltaX; x += sx; nx = -sx; ny = 0; nz = 0;
            }
            else if (tMaxY < tMaxZ)
            {
                t = tMaxY; tMaxY += tDeltaY; y += sy; nx = 0; ny = -sy; nz = 0;
            }
            else
            {
                t = tMaxZ; tMaxZ += tDeltaZ; z += sz; nx = 0; ny = 0; nz = -sz;
            }

            if (t > maxDist) return false;
            if (!world.TryGetBlock(x, y, z, out ushort block)) return false;
            if (block != 0)
            {
                hit = new BlockHit(x, y, z, nx, ny, nz, block);
                return true;
            }
        }
    }

    // расстояние по оси до первой границы вокселя и шаг между границами (в единицах луча)
    private static double AxisStart(double o, int cell, int step, double d, out double tDelta)
    {
        if (step == 0)
        {
            tDelta = double.PositiveInfinity;
            return double.PositiveInfinity;
        }
        tDelta = 1.0 / Math.Abs(d);
        double toBorder = step > 0 ? cell + 1 - o : o - cell;
        return toBorder * tDelta;
    }
}
