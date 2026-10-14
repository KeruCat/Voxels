namespace Game.Voxels;

/// <summary>Константы вокселей: чанк 16³, индексация внутри чанка.</summary>
public static class Vox
{
    public const int Size = 16;
    public const int Volume = Size * Size * Size;

    public static int Index(int x, int y, int z) => x | (y << 4) | (z << 8);
}
