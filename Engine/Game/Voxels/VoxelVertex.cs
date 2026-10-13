using System.Runtime.InteropServices;

namespace Game.Voxels;

/// <summary>
/// Вершина куска чанка: 8 байт.
/// Face: биты 0..2 — грань (0:+X 1:-X 2:+Y 3:-Y 4:+Z 5:-Z), биты 3..4 — ambient occlusion вершины (0 = самый тёмный угол, 3 = открыт).
/// U, V — размер слитого квада в вокселях (для тайлинга).
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct VoxelVertex
{
    public byte X, Y, Z;
    public byte Face;
    public byte U, V;       // 0..16
    public ushort BlockId;

    public readonly int FaceIndex => Face & 7;
    public readonly int AO => (Face >> 3) & 3;
}
