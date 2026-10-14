using Engine.Graphics;
using Engine.Memory;
using Game.Voxels;

namespace Game.World;

/// <summary>
/// Чанк — СТРУКТУРА, только неуправляемые поля (ключ, хэндлы пула, числа, bool).
/// Поэтому чанки лежат в плотном массиве, копируются по значению и безопасно уходят в CPU-пул.
/// Внутри нет тегов и «стадий»: только факты о чанке. Очередь, в которой он стоит, определяет, что с ним делать дальше.
/// </summary>
public struct Chunk
{
    public const int Size = Vox.Size;
    public const int Volume = Vox.Volume;

    public ChunkKey Key;

    // ── CPU ──
    public PoolHandle<ushort> Voxels;        // default = воздух (генератор вернул «пусто»)
    public bool Generated;
    /// <summary>Сколько из 26 соседей (внутри мира по Y) ещё не сгенерированы. 0 ⇒ можно мешить.</summary>
    public int PendingNeighbors;
    /// <summary>Меш уже запланирован или не нужен. Защищает от повторного меширования.</summary>
    public bool Meshed;

    public PoolHandle<VoxelVertex> MeshVertices;
    public PoolHandle<ushort> MeshIndices;
    public int MeshVertexCount, MeshIndexCount;

    // ── GPU ──
    public GpuRange Gpu;                     // Gpu.IndexCount > 0 ⇒ лежит на GPU
    public float FadeStart;                  // AppClock-время начала появления; Shown = false — ещё ни разу не показывался
    public bool Shown;                       // показывался ли уже: первое появление с анимацией только у чанков из генератора
    public bool Visible;
}
