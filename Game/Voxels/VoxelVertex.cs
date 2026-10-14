using System.Runtime.InteropServices;

namespace Game.Voxels;

/// <summary>
/// Вершина куска чанка: 8 байт.
///  X, Y, Z — позиция в чанке (0..16).
///  Face — биты 0..2: грань (0:+X 1:-X 2:+Y 3:-Y 4:+Z 5:-Z); биты 3..4: ambient occlusion вершины.
///  Packed (uint) — тайл атласа и геометрия текстуры:
///    биты 0..7   — номер тайла в атласе (строка-major, 10 колонок);
///    биты 8..12  — сколько тайлов укладывается по текстурной U (ширина слитого квада);
///    биты 13..17 — сколько тайлов по текстурной V (высота слитого квада);
///    бит 18      — угол квада по U (0 или 1);
///    бит 19      — угол квада по V (0 или 1).
///  Из этого шейдер сам считает UV: смещение тайла + угол × повтор.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct VoxelVertex
{
    public byte X, Y, Z;
    public byte Face;
    public uint Packed;

    public readonly int FaceIndex => Face & 7;
    public readonly int AO => (Face >> 3) & 3;
    public readonly int Tile => (int)(Packed & 0xFF);
    public readonly int RepeatU => (int)((Packed >> 8) & 31);
    public readonly int RepeatV => (int)((Packed >> 13) & 31);
    public readonly int CornerU => (int)((Packed >> 18) & 1);
    public readonly int CornerV => (int)((Packed >> 19) & 1);

    public static uint Pack(int tile, int repeatU, int repeatV, int cornerU, int cornerV) =>
        (uint)(tile | (repeatU << 8) | (repeatV << 13) | (cornerU << 18) | (cornerV << 19));
}
