namespace Game.Voxels;

/// <summary>Атлас блоков: сетка тайлов 16×16 px. Номер тайла = строка × Columns + столбец.</summary>
public static class Atlas
{
    public const int TilePx = 16;
    public const int Columns = 10;
    public const int Rows = 6;
    public const int Width = Columns * TilePx;   // 160
    public const int Height = Rows * TilePx;     // 96
}

/// <summary>Как поворачивается блок: печка — по сторонам света (4 положения), бревно — по оси (3 положения).</summary>
public enum RotationKind { None, Facing, Axis }

/// <summary>
/// Какой тайл показывать для блока и грани. Грани: 0:+X 1:-X 2:+Y(верх) 3:-Y(низ) 4:+Z 5:-Z.
/// Номера тайлов — по Blocks.png (сетка 10×6). Ориентация (facing) у печки — сторона света,
/// у бревна — ось: 0:Y (вертикально), 1:X (лёжа вдоль X), 2:Z (лёжа вдоль Z).
/// </summary>
public static class BlockTable
{
    public const ushort Log = 10;
    public const ushort Furnace = 11;

    public static RotationKind RotationOf(ushort type) => type switch
    {
        Furnace => RotationKind.Facing,
        Log => RotationKind.Axis,
        _ => RotationKind.None,
    };

    public static bool Rotates(ushort type) => RotationOf(type) != RotationKind.None;

    /// <summary>Грань печки, которая смотрит в сторону ориентации: 0:+Z 1:+X 2:-Z 3:-X.</summary>
    public static int FrontFace(int facing) => facing switch { 0 => 4, 1 => 0, 2 => 5, _ => 1 };

    /// <summary>Ось бревна (0:X 1:Y 2:Z) по значению ориентации.</summary>
    public static int AxisOf(int facing) => facing switch { 1 => 0, 2 => 2, _ => 1 };

    /// <summary>Значение ориентации, которое соответствует оси.</summary>
    public static int FacingOfAxis(int axis) => axis switch { 0 => 1, 2 => 2, _ => 0 };

    /// <summary>Ось волокон коры (если блок — бревно), иначе -1.</summary>
    public static int GrainAxis(ushort block) =>
        BlockState.Type(block) == Log ? AxisOf(BlockState.Facing(block)) : -1;

    // на каждой паре граней (d = 0:±X, 1:±Y, 2:±Z) вертикальная ось текстуры соответствует этой оси мира
    private static readonly int[] TextureVerticalAxis = { 1, 0, 1 };

    /// <summary>
    /// Нужно ли повернуть текстуру коры на 90° на грани d: волокна должны идти вдоль оси бревна.
    /// Торцы (грани вдоль оси) — кольца, их ориентация не важна.
    /// </summary>
    public static bool SwapGrain(ushort block, int d)
    {
        int grain = GrainAxis(block);
        if (grain < 0 || d == grain) return false;
        return grain != TextureVerticalAxis[d];
    }

    /// <summary>
    /// Ориентация при установке: печка смотрит на игрока; бревно ложится по нормали грани,
    /// к которой его приставляют (к боковой стене — вдоль стены, на землю — вертикально).
    /// </summary>
    public static int PlacementFacing(ushort type, float dx, float dz, int nx, int ny, int nz) => RotationOf(type) switch
    {
        RotationKind.Facing => BlockState.FacingToward(dx, dz),
        RotationKind.Axis => FacingOfAxis(nx != 0 ? 0 : nz != 0 ? 2 : 1),
        _ => 0,
    };

    /// <summary>Поворот по действию игрока: печка — на 90°, бревно — к следующей оси (Y → X → Z → Y).</summary>
    public static ushort Rotated(ushort value)
    {
        ushort type = BlockState.Type(value);
        int facing = BlockState.Facing(value);
        return RotationOf(type) switch
        {
            RotationKind.Facing => BlockState.Make(type, (facing + 1) & 3),
            RotationKind.Axis => BlockState.Make(type, (facing + 1) % 3),
            _ => value,
        };
    }

    public static int Tile(ushort block, int face)
    {
        ushort type = BlockState.Type(block);
        int facing = BlockState.Facing(block);

        if (type == Furnace)
            return face == FrontFace(facing) ? 36 : 37;   // лицо — в сторону поворота

        if (type == Log)
            return face / 2 == AxisOf(facing) ? 20 : 30;  // торцы — кольца, боковые грани — кора

        bool top = face == 2, bottom = face == 3;
        return type switch
        {
            1 => top ? 35 : bottom ? 34 : 31,     // трава: верх, низ (земля), бок с травой
            2 => 34,                              // земля
            3 => 37,                              // камень
            4 => 40,                              // песок
            5 => 38,                              // вода
            6 => 37,                              // снег: в атласе нет — временно камень
            7 => 0,                               // доски
            8 => 10,                              // листва (с прозрачностью)
            9 => 42,                              // каменные кирпичи
            _ => 37,
        };
    }
}
