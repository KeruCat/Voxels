namespace Engine.Voxels;

public sealed class WorldSettings
{
    public int Seed = 1337;
    public int RenderDistance = 10;     // радиус подгрузки по горизонтали, в чанках
    public int SpawnPerFrame = 512;     // сколько сущностей-чанков создавать за кадр (создание дешёвое)

    // Бюджеты на кадр. Системы работают, пока не выйдут за время, поэтому мир грузится
    // максимально быстро, но кадр не «залипает».
    public float GenBudgetMs = 4f;      // генерация вокселей (параллельно)
    public float MeshBudgetMs = 4f;     // меширование (параллельно)
    public float UploadBudgetMs = 2f;   // заливка мешей в GPU-арены
    public int UploadPerFrame = 64;     // и жёсткий потолок по количеству

}

/// <summary>Мир бесконечен по X/Z; по Y — ограниченная колонна из чанков.</summary>
public static class WorldLimits
{
    public const int MinChunkY = -2;
    public const int MaxChunkY = 6;
    public const int MinHeight = MinChunkY * Chunk.Size + 4;          // -28
    public const int MaxHeight = (MaxChunkY + 1) * Chunk.Size - 4;    // 108
}

public sealed class WorldStats
{
    public int Chunks, Drawn;
    public int OriginX, OriginY, OriginZ;
    public double PlayerX, PlayerY, PlayerZ;
}
