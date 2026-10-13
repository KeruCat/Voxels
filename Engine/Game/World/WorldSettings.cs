namespace Game.World;

public sealed class WorldSettings
{
    public int Seed = 1337;
    public int RenderDistance = 10;     // радиус показа по горизонтали, в чанках
    public int SpawnPerFrame = 512;     // сколько чанков создавать за кадр (создание дешёвое)

    // Бюджеты на кадр. Шаг останавливается, как только выйдет за время.
    public float GenBudgetMs = 4f;
    public float MeshBudgetMs = 4f;
    public float UploadBudgetMs = 2f;
    public int UploadPerFrame = 64;
}
