namespace Game.Voxels;

/// <summary>
/// Правила прозрачности блоков для мешера. Прозрачные блоки (вода, листва) не скрывают грани соседей,
/// поэтому грань земли или камня за листвой остаётся видимой. Грань скрывает только непрозрачный сосед
/// или сосед того же типа (листва внутри кроны не порождает внутренних граней).
/// </summary>
public static class BlockRules
{
    public const ushort Water = 5;
    public const ushort Leaves = 8;

    public static bool IsTransparent(ushort block) => block == Water || block == Leaves;

    /// <summary>Непрозрачный блок: не воздух и не прозрачный.</summary>
    public static bool IsOpaque(ushort block) => block != 0 && !IsTransparent(block);

    /// <summary>Скрывает ли сосед <paramref name="neighbour"/> грань блока типа <paramref name="type"/>.</summary>
    public static bool Occludes(ushort neighbour, ushort type) => IsOpaque(neighbour) || neighbour == type;
}
