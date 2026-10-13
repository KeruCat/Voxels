namespace Engine.Voxels;

public static class Chunk
{
    public const int Size = 16;
    public const int Volume = Size * Size * Size;   // 4096 вокселей * 2 байта = 8 КБ
    public static int Index(int x, int y, int z) => x | (y << 4) | (z << 8);
}
