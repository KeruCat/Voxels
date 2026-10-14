namespace Game.World;

/// <summary>
/// Все чанки мира: плотный массив структур + словарь «ключ → индекс».
/// Перебор по массиву — линейный и без промахов кэша; поиск соседа — один словарный запрос.
/// Внимание: индексы меняются при удалении (swap-remove), поэтому между вызовами их не храним, храним ключи.
/// </summary>
public sealed class ChunkStore
{
    private readonly Dictionary<ChunkKey, int> _index = new();
    private Chunk[] _items = new Chunk[1024];
    private int _count;

    public int Count => _count;
    public Span<Chunk> All => _items.AsSpan(0, _count);
    public ref Chunk this[int index] => ref _items[index];

    public bool Contains(ChunkKey key) => _index.ContainsKey(key);
    public bool TryGetIndex(ChunkKey key, out int index) => _index.TryGetValue(key, out index);

    public int Add(ChunkKey key, int pendingNeighbors)
    {
        if (_count == _items.Length) Array.Resize(ref _items, _items.Length * 2);
        int i = _count++;
        _items[i] = new Chunk { Key = key, PendingNeighbors = pendingNeighbors, FadeStart = -1f };
        _index.Add(key, i);
        return i;
    }

    /// <summary>Swap-remove. Возвращает копию удалённого, чтобы вызывающий освободил его ресурсы.</summary>
    public Chunk RemoveAt(int index)
    {
        Chunk removed = _items[index];
        _index.Remove(removed.Key);
        int last = _count - 1;
        _count--;
        if (index != last)
        {
            _items[index] = _items[last];
            _index[_items[index].Key] = index;
        }
        _items[last] = default;
        return removed;
    }
}
