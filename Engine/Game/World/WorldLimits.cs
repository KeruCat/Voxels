namespace Game.World;

/// <summary>Мир бесконечен по X/Z; по Y — ограниченная колонна чанков.</summary>
public static class WorldLimits
{
    public const int MinChunkY = -2;
    public const int MaxChunkY = 6;
    public const int MinHeight = MinChunkY * Chunk.Size + 4;          // -28
    public const int MaxHeight = (MaxChunkY + 1) * Chunk.Size - 4;    // 108

    public static bool InY(int y) => y >= MinChunkY && y <= MaxChunkY;
}
