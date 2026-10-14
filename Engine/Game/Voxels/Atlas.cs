namespace Game.Voxels;

/// <summary>Атлас блоков: сетка тайлов 16×16 px. Номер тайла = строка × Columns + столбец.</summary>
public static class Atlas
{
    public const int TilePx = 16;
    public const int Columns = 10;
    public const int Rows = 6;
    public const int Width = Columns * TilePx;   // 160
    public const int Height = Rows * TilePx;     // 96
}

/// <summary>
/// Какой тайл показывать для блока и грани. Грани: 0:+X 1:-X 2:+Y(верх) 3:-Y(низ) 4:+Z 5:-Z.
/// Номера тайлов — по Blocks.png (см. сетку 10×6).
/// </summary>
public static class BlockTable
{
    public static int Tile(ushort block, int face)
    {
        bool top = face == 2, bottom = face == 3;
        return block switch
        {
            1 => top ? 44 : bottom ? 34 : 31,     // трава: верх, низ (земля), бок с травой
            2 => 34,                              // земля
            3 => 37,                              // камень
            4 => 40,                              // песок
            5 => 38,                              // вода
            6 => 37,                              // снег: в атласе нет — временно камень
            7 => 0,                               // доски
            8 => 10,                              // листва (с прозрачностью)
            9 => 42,                              // каменные кирпичи
            10 => top || bottom ? 20 : 30,        // бревно: торец, кора
            _ => 37,
        };
    }
}
