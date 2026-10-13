using System.Runtime.InteropServices;

namespace Engine.Memory;

/// <summary>
/// Пул памяти: один непрерывный нативный блок, из которого выдаются куски под произвольные unmanaged-данные.
///
/// Как это устроено:
///  * Реальные данные лежат в блоке. Снаружи живёт только PoolHandle (индекс + поколение).
///  * Таблица handle-ов (_entries) связывает ключ с узлом (_nodes), а узел хранит смещение/размер.
///    Дефрагментация меняет только смещения в узлах — handle-ы остаются валидными.
///  * Узлы образуют двусвязный список в порядке адресов (занятые + свободные), свободные соседи всегда слиты.
///  * Пул умеет расти/сжиматься (Resize / Trim / автоматический рост в Allocate).
///
/// ВАЖНО: ref/Span/указатели, полученные из пула, валидны только до следующего вызова, который может
/// переместить данные: Allocate*, Reallocate, Defragment, Resize, Trim. Держи в коде handle, а не ref.
/// Если нужен стабильный адрес на время (например, upload в GPU) — используй Pin().
///
/// Потокобезопасности нет: либо один поток, либо внешний lock.
/// </summary>
public sealed unsafe partial class MemoryPool : IDisposable
{
    private struct Node
    {
        public long Offset, Size;
        public int Prev, Next;
        public int Entry;           // -1 = свободный блок
    }

    private struct Entry
    {
        public int Node;            // -1 = запись свободна
        public int Generation;
        public int Pins;
        public int NextFree;        // цепочка свободных записей
        public long Requested;      // реально запрошенный размер в байтах (Size узла округлён по выравниванию)
    }

    private byte* _base;
    private long _capacity;
    private readonly int _alignment;
    private readonly long _maxCapacity;

    private Node[] _nodes = new Node[32];
    private int _nodeCount;
    private readonly Stack<int> _freeNodes = new();
    private int _head = -1, _tail = -1;

    private Entry[] _entries = new Entry[32];
    private int _entryCount;
    private int _freeEntry = -1;

    private long _used;
    private int _live;
    private int _pinTotal;

    /// <summary>Автоматически расти, если не хватило места (после попытки дефрагментации).</summary>
    public bool AutoGrow { get; set; } = true;
    /// <summary>Перед ростом пробовать дефрагментировать, если суммарно свободного места хватает.</summary>
    public bool AutoDefragment { get; set; } = true;
    /// <summary>Во сколько раз минимум увеличивать ёмкость при авто-росте.</summary>
    public double GrowthFactor { get; set; } = 2.0;

    public long Capacity => _capacity;
    public long UsedBytes => _used;
    public long FreeBytes => _capacity - _used;
    public int LiveAllocations => _live;
    public int Alignment => _alignment;

    public MemoryPool(long initialCapacity, int alignment = 16, long maxCapacity = long.MaxValue)
    {
        if (alignment <= 0 || (alignment & (alignment - 1)) != 0)
            throw new ArgumentException("Alignment must be a power of two.", nameof(alignment));
        if (initialCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(initialCapacity));

        _alignment = alignment;
        _capacity = AlignUp(initialCapacity);
        _maxCapacity = Math.Max(maxCapacity & ~(long)(alignment - 1), _capacity);

        _base = (byte*)NativeMemory.AlignedAlloc((nuint)_capacity, (nuint)_alignment);
        if (_base == null) throw new OutOfMemoryException();

        int n = NewNode(0, _capacity, -1);
        _head = _tail = n;
    }
}
