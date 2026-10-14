namespace Engine.Core;

/// <summary>
/// Floating origin: мировые координаты хранятся в double, а на GPU уходят значения относительно начала отсчёта,
/// измеренного в ячейках (CellSize единиц мира). Начало «прыгает» только когда объект ушёл дальше RebaseCells ячеек.
/// Движок не знает, что такое чанк: ячейка задаётся игрой.
/// </summary>
public sealed class FloatingOrigin
{
    public const int RebaseCells = 2;

    public readonly int CellSize;
    public int X, Y, Z;

    public FloatingOrigin(int cellSize) => CellSize = cellSize;

    public void Follow(double wx, double wy, double wz)
    {
        X = Snap(X, wx);
        Y = Snap(Y, wy);
        Z = Snap(Z, wz);
    }

    public double ToLocalX(double wx) => wx - (double)X * CellSize;
    public double ToLocalY(double wy) => wy - (double)Y * CellSize;
    public double ToLocalZ(double wz) => wz - (double)Z * CellSize;

    private int Snap(int origin, double world)
    {
        int c = (int)Math.Floor(world / CellSize);
        return Math.Abs(c - origin) > RebaseCells ? c : origin;
    }
}
