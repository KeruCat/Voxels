using Engine.Voxels;

namespace Engine.Core;

/// <summary>
/// Floating origin. Истинные координаты игрока хранятся в double (WorldPosition), а всё, что уходит на GPU,
/// считается относительно этого начала отсчёта (в чанках) и поэтому остаётся маленьким и точным в float.
/// Начало отсчёта «прыгает» только когда игрок отошёл от него дальше RebaseChunks чанков;
/// двигать при этом ничего не нужно: чанки хранят целые координаты, локальные float-позиции пересчитываются каждый кадр.
/// </summary>
internal sealed class FloatingOrigin
{
    public const int RebaseChunks = 2;

    public int X, Y, Z;   // координаты чанка, который считается (0,0,0) в render-пространстве

    public void Follow(double wx, double wy, double wz)
    {
        X = Snap(X, wx);
        Y = Snap(Y, wy);
        Z = Snap(Z, wz);
    }

    public double ToLocalX(double wx) => wx - (double)X * Chunk.Size;
    public double ToLocalY(double wy) => wy - (double)Y * Chunk.Size;
    public double ToLocalZ(double wz) => wz - (double)Z * Chunk.Size;

    private static int Snap(int origin, double world)
    {
        int c = (int)Math.Floor(world / Chunk.Size);
        return Math.Abs(c - origin) > RebaseChunks ? c : origin;
    }
}
