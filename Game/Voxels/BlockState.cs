namespace Game.Voxels;

/// <summary>
/// Значение блока = тип (биты 0..9) + ориентация (биты 10..11). Ориентация — 4 направления (поворот блока).
/// Поворот не требует метаданных: мешер и так сравнивает блоки по значению, разные ориентации не склеиваются.
/// Воздух — значение 0.
/// </summary>
public static class BlockState
{
    public const int TypeBits = 10;
    public const ushort TypeMask = (1 << TypeBits) - 1;

    public static ushort Type(ushort value) => (ushort)(value & TypeMask);

    public static int Facing(ushort value) => (value >> TypeBits) & 3;

    public static ushort Make(ushort type, int facing) =>
        (ushort)((type & TypeMask) | ((facing & 3) << TypeBits));

    /// <summary>Убирает ориентацию у неповорачиваемых блоков: одинаковые блоки имеют одно значение и склеиваются.</summary>
    public static ushort Normalize(ushort value)
    {
        ushort type = Type(value);
        return BlockTable.RotationOf(type) switch
        {
            RotationKind.None => type,
            RotationKind.Axis when Facing(value) == 3 => Make(type, 0),
            _ => value,
        };
    }

    /// <summary>Ориентация, при которой лицо смотрит в направлении (dx, dz). Ничьи решаются в пользу Z.</summary>
    public static int FacingToward(float dx, float dz)
    {
        if (MathF.Abs(dx) > MathF.Abs(dz)) return dx > 0 ? 1 : 3;
        return dz > 0 ? 0 : 2;
    }
}
