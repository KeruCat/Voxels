namespace Game.Voxels.Meshing;

public sealed class GreedyMesher
{
    private const int N = Vox.Size;
    public const int MaxQuads = 3 * (N + 1) * N * N;           // 13056

    public readonly VoxelVertex[] Vertices = new VoxelVertex[MaxQuads * 4];   // 52224 < 65536 → индексы ushort
    public readonly ushort[] Indices = new ushort[MaxQuads * 6];
    public int VertexCount { get; private set; }
    public int IndexCount { get; private set; }
    public int QuadCount => VertexCount / 4;

    /// <summary>Лицевая грань = против часовой (правосторонняя система). Поставь false, если API ждёт CW.</summary>
    public bool CounterClockwiseFront { get; set; } = true;

    /// <summary>Считать ambient occlusion (иначе все вершины AO=3 и склейка максимальная).</summary>
    public bool AmbientOcclusion { get; set; } = true;

    /// <summary>Воксель за границей чанка (x, y, z вне 0..15, в координатах этого чанка). null = воздух.</summary>
    public Func<int, int, int, ushort>? OutsideSampler { get; set; }

    private readonly int[] _mask = new int[N * N];
    private readonly int[] _p = new int[3];

    public void Mesh(ReadOnlySpan<ushort> voxels)
    {
        if (voxels.Length < Vox.Volume) throw new ArgumentException("Need 16^3 voxels.", nameof(voxels));
        VertexCount = IndexCount = 0;

        Span<int> x = stackalloc int[3];
        Span<int> q = stackalloc int[3];

        for (int d = 0; d < 3; d++)
        {
            int u = (d + 1) % 3, v = (d + 2) % 3;
            q.Clear(); q[d] = 1;

            for (x[d] = -1; x[d] < N; x[d]++)
            for (int pass = 0; pass < 2; pass++)      // 0 — грани стороны A (+d), 1 — грани стороны B (-d)
            {
                int n = 0;
                for (x[v] = 0; x[v] < N; x[v]++)
                    for (x[u] = 0; x[u] < N; x[u]++)
                    {
                        bool inA = x[d] >= 0, inB = x[d] < N - 1;
                        int a = inA ? voxels[Vox.Index(x[0], x[1], x[2])] : 0;
                        int b = inB ? voxels[Vox.Index(x[0] + q[0], x[1] + q[1], x[2] + q[2])] : 0;

                        // сосед за границей чанка — блок другого чанка: он может скрыть грань, но не создаёт её
                        ushort aOcc = inA ? (ushort)a : OutsideBlock(x[0], x[1], x[2]);
                        ushort bOcc = inB ? (ushort)b : OutsideBlock(x[0] + q[0], x[1] + q[1], x[2] + q[2]);

                        // обе стороны считаются независимо: между двумя прозрачными блоками видны обе грани
                        int c;
                        bool front;
                        if (pass == 0 && a != 0 && !BlockRules.Occludes(bOcc, (ushort)a)) { c = a; front = true; }
                        else if (pass == 1 && b != 0 && !BlockRules.Occludes(aOcc, (ushort)b)) { c = b; front = false; }
                        else { _mask[n++] = 0; continue; }

                        int ao = 0xFF;   // 4 угла по 2 бита, все = 3
                        if (AmbientOcclusion)
                            ao = ComputeAO(voxels, d, u, v, front ? x[d] + 1 : x[d], x[u], x[v]);

                        int packed = c | (ao << 16);
                        _mask[n++] = front ? packed : -packed;
                    }

                n = 0;
                for (int j = 0; j < N; j++)
                {
                    for (int i = 0; i < N;)
                    {
                        int c = _mask[n];
                        if (c == 0) { i++; n++; continue; }

                        int ao = (Math.Abs(c) >> 16) & 0xFF;

                        // вдоль направления склейки AO не должен меняться
                        int w = 1;
                        if (UniformU(ao))
                            while (i + w < N && _mask[n + w] == c) w++;

                        int h = 1;
                        if (UniformV(ao))
                            for (bool stop = false; j + h < N && !stop; h++)
                                for (int k = 0; k < w; k++)
                                    if (_mask[n + k + h * N] != c) { stop = true; h--; break; }

                        EmitQuad(d, u, v, x[d] + 1, i, j, w, h, c);

                        for (int l = 0; l < h; l++)
                            for (int k = 0; k < w; k++)
                                _mask[n + k + l * N] = 0;

                        i += w; n += w;
                    }
                }
            }
        }
    }

    // ───────────── AO ─────────────

    internal static bool UniformU(int ao) => ((ao) & 3) == ((ao >> 2) & 3) && ((ao >> 6) & 3) == ((ao >> 4) & 3);
    internal static bool UniformV(int ao) => ((ao) & 3) == ((ao >> 6) & 3) && ((ao >> 2) & 3) == ((ao >> 4) & 3);

    private ushort OutsideBlock(int x, int y, int z) => OutsideSampler != null ? OutsideSampler(x, y, z) : (ushort)0;

    private bool Solid(ReadOnlySpan<ushort> voxels, int d, int u, int v, int layer, int uu, int vv)
    {
        _p[d] = layer; _p[u] = uu; _p[v] = vv;
        int x = _p[0], y = _p[1], z = _p[2];
        if ((uint)x < N && (uint)y < N && (uint)z < N)
            return BlockRules.IsOpaque(voxels[Vox.Index(x, y, z)]);
        return BlockRules.IsOpaque(OutsideBlock(x, y, z));
    }

    /// <summary>AO четырёх углов грани: p0 (-,-), p1 (+,-), p2 (+,+), p3 (-,+) по осям (u, v); layer — слой воздуха перед гранью.</summary>
    private int ComputeAO(ReadOnlySpan<ushort> voxels, int d, int u, int v, int layer, int i, int j)
    {
        return CornerAO(voxels, d, u, v, layer, i, j, -1, -1)
             | (CornerAO(voxels, d, u, v, layer, i, j, +1, -1) << 2)
             | (CornerAO(voxels, d, u, v, layer, i, j, +1, +1) << 4)
             | (CornerAO(voxels, d, u, v, layer, i, j, -1, +1) << 6);
    }

    private int CornerAO(ReadOnlySpan<ushort> voxels, int d, int u, int v, int layer, int i, int j, int su, int sv)
    {
        bool s1 = Solid(voxels, d, u, v, layer, i + su, j);
        bool s2 = Solid(voxels, d, u, v, layer, i, j + sv);
        bool cr = Solid(voxels, d, u, v, layer, i + su, j + sv);
        return (s1 && s2) ? 0 : 3 - ((s1 ? 1 : 0) + (s2 ? 1 : 0) + (cr ? 1 : 0));
    }

    // ───────────── Вывод квада ─────────────

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

        int vb = VertexCount;
        Set(vb + 0, d, u, v, plane, i,     j,     face, a0, 0, 0, texW, texH, tile);
        Set(vb + 1, d, u, v, plane, i + w, j,     face, a1, 1, 0, texW, texH, tile);
        Set(vb + 2, d, u, v, plane, i + w, j + h, face, a2, 1, 1, texW, texH, tile);
        Set(vb + 3, d, u, v, plane, i,     j + h, face, a3, 0, 1, texW, texH, tile);

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

        private void Set(int idx, int d, int u, int v, int pd, int pu, int pv, int face, int ao, int cu, int cv, int texW, int texH, int tile)
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
        vert.Packed = VoxelVertex.Pack(tile, texW, texH, cuT, cvT);
    }
}
