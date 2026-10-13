namespace Game.World;

/// <summary>
/// Очередь ключей для одного шага конвейера. Каждый ключ лежит в очереди не больше одного раза:
/// повторный Enqueue игнорируется (иначе при частых пересчётах центра очередь разрастается дублями).
/// TakeBest отдаёт k лучших по приоритету и выбрасывает невалидные записи.
/// </summary>
internal sealed class KeyQueue
{
    private readonly List<ChunkKey> _items = new();
    private readonly HashSet<ChunkKey> _members = new();     // что уже стоит в очереди
    private readonly PriorityQueue<ChunkKey, float> _heap = new();
    private readonly HashSet<ChunkKey> _taken = new();

    public int Count => _items.Count;

    public void Enqueue(ChunkKey key)
    {
        if (_members.Add(key)) _items.Add(key);
    }

    /// <summary>Записывает до k лучших ключей в dest (лучший — dest[0]) и удаляет их из очереди. Возвращает их число.</summary>
    public int TakeBest(ChunkPriority prio, int k, Func<ChunkKey, bool> valid, ChunkKey[] dest)
    {
        _heap.Clear();
        int w = 0;
        for (int r = 0; r < _items.Count; r++)
        {
            var key = _items[r];
            if (!valid(key))
            {
                _members.Remove(key);                 // невалидная запись уходит из очереди
                continue;
            }
            _items[w++] = key;

            float p = prio.Of(key);
            if (_heap.Count < k) _heap.Enqueue(key, -p);
            else if (_heap.TryPeek(out _, out float negWorst) && p < -negWorst) _heap.EnqueueDequeue(key, -p);
        }
        _items.RemoveRange(w, _items.Count - w);

        int count = _heap.Count;
        _taken.Clear();
        for (int j = count - 1; j >= 0; j--)
        {
            var key = _heap.Dequeue();
            dest[j] = key;
            _taken.Add(key);
            _members.Remove(key);
        }

        if (count > 0)
        {
            w = 0;
            for (int r = 0; r < _items.Count; r++)
                if (!_taken.Contains(_items[r])) _items[w++] = _items[r];
            _items.RemoveRange(w, _items.Count - w);
        }
        return count;
    }
}