using System.Numerics;

namespace Game.Voxels.Meshing;

/// <summary>
/// Бинарный greedy-мешер. Тот же алгоритм склейки и тот же AO, что у GreedyMesher, но вычисляется побитово:
///  * строки граней — 16-битные маски; видимость граней = «свой блок с одной стороны и воздух с другой» одной операцией;
///  * склейка по ширине и высоте — через ctz и сравнение побитовых ключей, без попиксельных циклов;
///  * AO четырёх углов считается для всей плоскости сразу, двумя битовыми плоскостями на угол.
/// Результат совпадает с GreedyMesher (проверяется тестом по набору квадов). Публичный API тот же.
/// </summary>
public sealed class BinaryGreedyMesher
{
    private const int N = Vox.Size;
    private const int P = N + 2;                       // чанк + один слой вокруг
    public const int MaxQuads = 3 * (N + 1) * N * N;

    public readonly VoxelVertex[] Vertices = new VoxelVertex[MaxQuads * 4];
    public readonly ushort[] Indices = new ushort[MaxQuads * 6];
    public int VertexCount { get; private set; }
    public int IndexCount { get; private set; }
    public int QuadCount => VertexCount / 4;

    /// <summary>Лицевая грань = против часовой (как в GreedyMesher).</summary>
    public bool CounterClockwiseFront { get; set; } = true;

    /// <summary>Считать ambient occlusion (иначе все вершины AO=3).</summary>
    public bool AmbientOcclusion { get; set; } = true;

    /// <summary>Воксель за границей чанка (координаты этого чанка). null = воздух.</summary>
    public Func<int, int, int, ushort>? OutsideSampler { get; set; }

    // padded-твёрдость: чанк + слой вокруг, индекс PIdx(x,y,z)
    private readonly ushort[] _blk = new ushort[P * P * P];          // блоки с рамкой (0 = воздух)
    // строки твёрдости по направлениям: [d, слой+1, v+1] → бит (u+1)
    private int[][,,] _occ = new int[8][,,];                   // [тип][d, слой+1, v+1] → клетки, скрывающие грань этого типа
    private readonly int[,,] _rows = new int[3, P, P];
    // маски блоков внутри чанка: [тип, d, слой, v] → бит u
    private ushort[] _tr = new ushort[8 * 3 * N * N];
    private ushort[] _types = new ushort[8];
    private readonly int[] _typeIdx = new int[65536];  // блок → индекс типа, -1 = нет
    private int _typeCount;

    private readonly ushort[,] _aoFront = new ushort[8, N];
    private readonly ushort[,] _aoBack = new ushort[8, N];
    private readonly ushort[] _mask = new ushort[N];
    private readonly int[] _p = new int[3];

    public BinaryGreedyMesher() => Array.Fill(_typeIdx, -1);

    public void Mesh(ReadOnlySpan<ushort> voxels)
    {
        if (voxels.Length < Vox.Volume) throw new ArgumentException("Need 16^3 voxels.", nameof(voxels));
        VertexCount = IndexCount = 0;

        BuildSolid(voxels);
        BuildTypes(voxels);
        BuildOccluders();

        for (int d = 0; d < 3; d++)
        for (int p = 0; p <= N; p++)
        {
            ComputeAO(d, p, _aoFront);          // грань +d: воздух в слое p
            ComputeAO(d, p - 1, _aoBack);       // грань -d: воздух в слое p-1
            for (int t = 0; t < _typeCount; t++)
            {
                ushort block = _types[t];
                Plane(d, p, t, block, true, _aoFront);
                Plane(d, p, t, block, false, _aoBack);
            }
        }

        for (int t = 0; t < _typeCount; t++) _typeIdx[_types[t]] = -1;
        _typeCount = 0;
    }

    // ───────────── подготовка данных ─────────────

    private static int PIdx(int x, int y, int z) => ((x + 1) * P + (y + 1)) * P + (z + 1);

    private ushort OutsideBlock(int x, int y, int z) => OutsideSampler != null ? OutsideSampler(x, y, z) : (ushort)0;

    /// <summary>Для каждого типа блока: клетки, которые скрывают его грань (непрозрачные или того же типа).</summary>
    private void BuildOccluders()
    {
        if (_occ.Length < _typeCount) Array.Resize(ref _occ, _typeCount * 2);
        for (int t = 0; t < _typeCount; t++)
        {
            _occ[t] ??= new int[3, P, P];
            ushort type = _types[t];
            for (int d = 0; d < 3; d++)
            for (int L = -1; L <= N; L++)
            for (int v = -1; v <= N; v++)
            {
                int row = 0;
                for (int u = -1; u <= N; u++)
                {
                    int x = d == 0 ? L : d == 1 ? v : u;
                    int y = d == 0 ? u : d == 1 ? L : v;
                    int z = d == 0 ? v : d == 1 ? u : L;
                    if (BlockRules.Occludes(_blk[PIdx(x, y, z)], type)) row |= 1 << (u + 1);
                }
                _occ[t][d, L + 1, v + 1] = row;
            }
        }
    }

    private void BuildSolid(ReadOnlySpan<ushort> voxels)
    {
        for (int x = -1; x <= N; x++)
        for (int y = -1; y <= N; y++)
        for (int z = -1; z <= N; z++)
        {
            bool inside = (uint)x < N && (uint)y < N && (uint)z < N;
            _blk[PIdx(x, y, z)] = inside ? voxels[Vox.Index(x, y, z)] : OutsideBlock(x, y, z);
        }

        for (int d = 0; d < 3; d++)
        for (int L = -1; L <= N; L++)
        for (int v = -1; v <= N; v++)
        {
            int row = 0;
            for (int u = -1; u <= N; u++)
            {
                // d: 0 → (L,u,v) = (x,y,z); 1 → (v,L,u); 2 → (u,v,L)
                int x = d == 0 ? L : d == 1 ? v : u;
                int y = d == 0 ? u : d == 1 ? L : v;
                int z = d == 0 ? v : d == 1 ? u : L;
                if (BlockRules.IsOpaque(_blk[PIdx(x, y, z)])) row |= 1 << (u + 1);
            }
            _rows[d, L + 1, v + 1] = row;
        }
    }

    private void BuildTypes(ReadOnlySpan<ushort> voxels)
    {
        _typeCount = 0;
        for (int x = 0; x < N; x++)
        for (int y = 0; y < N; y++)
        for (int z = 0; z < N; z++)
        {
            ushort val = voxels[Vox.Index(x, y, z)];
            if (val == 0) continue;

            int t = _typeIdx[val];
            if (t < 0) t = AddType(val);
            int tb = t * 3 * N * N;

            // d=0: слой x, v=z, бит y; d=1: слой y, v=x, бит z; d=2: слой z, v=y, бит x
            _tr[tb + x * N + z] |= (ushort)(1 << y);
            _tr[tb + N * N + y * N + x] |= (ushort)(1 << z);
            _tr[tb + 2 * N * N + z * N + y] |= (ushort)(1 << x);
        }
    }

    private int AddType(ushort val)
    {
        int t = _typeCount++;
        if (t >= _types.Length) Array.Resize(ref _types, _types.Length * 2);
        _types[t] = val;
        _typeIdx[val] = t;

        int need = (t + 1) * 3 * N * N;
        if (need > _tr.Length) Array.Resize(ref _tr, Math.Max(need, _tr.Length * 2));
        Array.Clear(_tr, t * 3 * N * N, 3 * N * N);
        return t;
    }

    // ───────────── AO для всей плоскости ─────────────

    private static readonly int[] CornerSu = [-1, +1, +1, -1];
    private static readonly int[] CornerSv = [-1, -1, +1, +1];

    /// <summary>Растяжение padded-строки на смещение su: бит i = значение клетки (i+su).</summary>
    private static int Vec(int padRow, int su) => (padRow >> (su + 1)) & 0xFFFF;

    /// <summary>
    /// AO четырёх углов для всех клеток плоскости. Для угла k: s1 = (i+su, j), s2 = (i, j+sv), cr = (i+su, j+sv).
    /// value = (s1&amp;s2) ? 0 : 3 - (s1+s2+cr). Двоичное кодирование значения: bit0 и bit1 — отдельные плоскости.
    /// Плоскость ao[2k] — младший бит угла k, ao[2k+1] — старший; это совпадает с битами ключа AO.
    /// </summary>
    private void ComputeAO(int d, int air, ushort[,] ao)
    {
        if (!AmbientOcclusion)
        {
            for (int q = 0; q < 8; q++)
            for (int j = 0; j < N; j++) ao[q, j] = 0xFFFF;   // все углы = 3
            return;
        }

        int rowAir = air + 1;
        for (int j = 0; j < N; j++)
        for (int k = 0; k < 4; k++)
        {
            int su = CornerSu[k], sv = CornerSv[k];
            int x = Vec(_rows[d, rowAir, j + 1], su);          // s1
            int y = Vec(_rows[d, rowAir, j + sv + 1], 0);      // s2
            int z = Vec(_rows[d, rowAir, j + sv + 1], su);     // cr

            int nx = ~(x | y) & 0xFFFF;                         // ни s1, ни s2
            int xo = x ^ y;                                     // ровно одна из s1, s2
            ao[2 * k, j] = (ushort)(((nx & ~z) | (xo & z)) & 0xFFFF);
            ao[2 * k + 1, j] = (ushort)((nx | (xo & ~z)) & 0xFFFF);
        }
    }

    // ───────────── грани одного типа в одной плоскости ─────────────

    private void Plane(int d, int p, int t, ushort block, bool front, ushort[,] ao)
    {
        int tb = t * 3 * N * N + d * N * N;
        bool any = false;
        for (int v = 0; v < N; v++)
        {
            int m;
            if (front)
            {
                // грань +d: свой блок в слое p-1, воздух (не твёрдое) в слое p
                int inA = p >= 1 ? _tr[tb + (p - 1) * N + v] : 0;
                int solidB = (_occ[t][d, p + 1, v + 1] >> 1) & 0xFFFF;
                m = inA & ~solidB;
            }
            else
            {
                // грань -d: свой блок в слое p, воздух в слое p-1
                int inB = p <= N - 1 ? _tr[tb + p * N + v] : 0;
                int solidA = (_occ[t][d, p, v + 1] >> 1) & 0xFFFF;
                m = inB & ~solidA;
            }
            _mask[v] = (ushort)(m & 0xFFFF);
            any |= m != 0;
        }
        if (any) Greedy(d, p, block, front, ao);
    }

    /// <summary>Побитовый ключ AO клетки (i,j): бит q = плоскость q.</summary>
    private static int Key(ushort[,] ao, int i, int j)
    {
        int k = 0;
        for (int q = 0; q < 8; q++) k |= ((ao[q, j] >> i) & 1) << q;
        return k;
    }

    /// <summary>Битовая маска клеток строки j, у которых AO-ключ равен k.</summary>
    private static int EqRow(ushort[,] ao, int j, int k)
    {
        int m = 0xFFFF;
        for (int q = 0; q < 8; q++)
            m &= ((k >> q) & 1) != 0 ? ao[q, j] : ~ao[q, j];
        return m & 0xFFFF;
    }

    /// <summary>
    /// Та же склейка, что в GreedyMesher: по строке — пока ключ совпадает и AO однороден вдоль u;
    /// вверх — пока все клетки строки совпадают с ключом и AO однороден вдоль v.
    /// Порядок обхода (j снаружи, i внутри) тот же, поэтому квады совпадают.
    /// </summary>
    private void Greedy(int d, int p, ushort block, bool front, ushort[,] ao)
    {
        int u = (d + 1) % 3, v = (d + 2) % 3;
        var M = _mask;

        for (int j = 0; j < N; j++)
        {
            while (M[j] != 0)
            {
                int i = BitOperations.TrailingZeroCount((int)M[j]);
                int k = Key(ao, i, j);

                int w = 1;
                if (GreedyMesher.UniformU(k))
                {
                    int same = M[j] & EqRow(ao, j, k);
                    w = BitOperations.TrailingZeroCount(~(same >> i));
                }

                int wm = ((1 << w) - 1) << i;
                int h = 1;
                if (GreedyMesher.UniformV(k))
                {
                    for (int r = j + 1; r < N; r++)
                    {
                        if (((M[r] & EqRow(ao, r, k)) & wm) != wm) break;
                        h++;
                    }
                }

                int packed = block | (k << 16);
                EmitQuad(d, u, v, p, i, j, w, h, front ? packed : -packed);

                for (int r = j; r < j + h; r++) M[r] = (ushort)(M[r] & ~wm);
            }
        }
    }

    // ───────────── вывод квада (как в GreedyMesher) ─────────────

        private void EmitQuad(int d, int u, int v, int plane, int i, int j, int w, int h, int c)
    {
        int abs = Math.Abs(c);
        ushort block = (ushort)(abs & 0xFFFF);
        int ao = (abs >> 16) & 0xFF;
        int a0 = ao & 3, a1 = (ao >> 2) & 3, a2 = (ao >> 4) & 3, a3 = (ao >> 6) & 3;
        int face = d * 2 + (c > 0 ? 0 : 1);

        // тайл по блоку и грани; на гранях ±X горизонталь текстуры — ось Z, поэтому повтор по U/V меняется местами
        int tile = BlockTable.Tile(block, face);
        int texW = d == 0 ? h : w;
        int texH = d == 0 ? w : h;
        bool swap = BlockTable.SwapGrain(block, d);

        int vb = VertexCount;
        Set(vb + 0, d, u, v, plane, i,     j,     face, a0, 0, 0, texW, texH, tile, swap);
        Set(vb + 1, d, u, v, plane, i + w, j,     face, a1, 1, 0, texW, texH, tile, swap);
        Set(vb + 2, d, u, v, plane, i + w, j + h, face, a2, 1, 1, texW, texH, tile, swap);
        Set(vb + 3, d, u, v, plane, i,     j + h, face, a3, 0, 1, texW, texH, tile, swap);

        bool altDiagonal = a0 + a2 > a1 + a3;
        bool reversed = (c < 0) == CounterClockwiseFront;
        int[] t = altDiagonal
            ? (reversed ? AltRev : Alt)
            : (reversed ? DefRev : Def);

        int ib = IndexCount;
        for (int k = 0; k < 6; k++) Indices[ib + k] = (ushort)(vb + t[k]);

        VertexCount += 4;
        IndexCount += 6;
    }

    private static readonly int[] Def = [0, 1, 2, 0, 2, 3];
    private static readonly int[] DefRev = [0, 2, 1, 0, 3, 2];
    private static readonly int[] Alt = [1, 2, 3, 1, 3, 0];
    private static readonly int[] AltRev = [1, 3, 2, 1, 0, 3];

        private void Set(int idx, int d, int u, int v, int pd, int pu, int pv, int face, int ao, int cu, int cv, int texW, int texH, int tile, bool swap)
    {
        ref VoxelVertex vert = ref Vertices[idx];
        // позиция: ось d = pd, ось u = pu, ось v = pv
        int ax0 = d == 0 ? pd : u == 0 ? pu : pv;
        int ax1 = d == 1 ? pd : u == 1 ? pu : pv;
        int ax2 = d == 2 ? pd : u == 2 ? pu : pv;
        vert.X = (byte)ax0; vert.Y = (byte)ax1; vert.Z = (byte)ax2;
        vert.Face = (byte)(face | (ao << 3));
        // угол в координатах текстуры. Вертикаль текстуры (V) на боковых гранях перевёрнута:
        // строка 0 тайла — верх, поэтому верх блока (большой Y) должен попадать на строку 0.
        int cuT, cvT;
        if (d == 0) { cuT = cv; cvT = 1 - cu; }     // ±X: горизонталь = Z (ось v плоскости), вертикаль = Y (ось u)
        else if (d == 2) { cuT = cu; cvT = 1 - cv; } // ±Z: горизонталь = X (ось u), вертикаль = Y (ось v)
        else { cuT = cu; cvT = cv; }                 // ±Y: верх/низ, ориентация не важна
        // кора на бревне: волокна вдоль оси бревна — текстура поворачивается на 90°
        if (swap) { (cuT, cvT) = (cvT, cuT); (texW, texH) = (texH, texW); }
        vert.Packed = VoxelVertex.Pack(tile, texW, texH, cuT, cvT);
    }
}
