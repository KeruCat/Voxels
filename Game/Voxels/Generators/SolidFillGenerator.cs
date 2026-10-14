namespace Game.Voxels.Generators;

/// <summary>Заливка всего чанка одним типом блока.</summary>
public sealed class SolidFillGenerator : IChunkGenerator
{
    private readonly ushort _block;
    public SolidFillGenerator(ushort blockId = 1) => _block = blockId;

    public bool Fill(Span<ushort> voxels, int cx, int cy, int cz)
    {
        voxels.Slice(0, Vox.Volume).Fill(_block);
        return _block != 0;
    }
}
