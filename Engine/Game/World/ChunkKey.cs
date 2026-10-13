namespace Game.World;

/// <summary>Координаты чанка. Ключ словаря и элемент очередей.</summary>
public readonly record struct ChunkKey(int X, int Y, int Z)
{
    public ChunkKey Plus(ChunkKey d) => new(X + d.X, Y + d.Y, Z + d.Z);
}
