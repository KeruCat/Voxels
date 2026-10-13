namespace Engine.Voxels.Generators;

public interface IChunkGenerator
{
    /// <summary>Записывает ВСЕ 16^3 вокселей. Возвращает false, если чанк целиком пустой (воздух).</summary>
    bool Fill(Span<ushort> voxels, int chunkX, int chunkY, int chunkZ);
}
