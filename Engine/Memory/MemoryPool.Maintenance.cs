using System.Runtime.InteropServices;

namespace Engine.Memory;

public sealed unsafe partial class MemoryPool
{
    // ───────────────────────────── Дефрагментация ─────────────────────────────

    /// <summary>
    /// Сдвигает занятые блоки к началу пула, собирая свободное место в один кусок в конце.
    /// Закреплённые (Pin) блоки не двигаются. maxBytesToMove позволяет делать дефрагментацию
    /// по частям (например, по 1 МБ за кадр): вызывай, пока Completed == false.
    /// Каждый блок копируется максимум один раз за полный проход.
    /// </summary>
    public DefragResult Defragment(long maxBytesToMove = long.MaxValue)
    {
        ThrowIfDisposed();
        long movedBytes = 0;
        int movedBlocks = 0;
        int f = _head;

        while (true)
        {
            while (f != -1 && _nodes[f].Entry != -1) f = _nodes[f].Next;   // ищем свободный блок
            if (f == -1) break;

            int u = _nodes[f].Next;                                         // за ним — занятый (свободные слиты)
            if (u == -1) break;                                             // свободный хвост → всё уплотнено

            int e = _nodes[u].Entry;
            if (_entries[e].Pins > 0) { f = _nodes[u].Next; continue; }     // закреплённый — обходим
            if (movedBytes >= maxBytesToMove) return new DefragResult(movedBytes, movedBlocks, false);

            long fo = _nodes[f].Offset;
            long us = _nodes[u].Size;
            long req = _entries[e].Requested;
            Buffer.MemoryCopy(_base + _nodes[u].Offset, _base + fo, us, req);   // корректно для перекрытия

            // порядок было: p, f, u, nx  →  стало: p, u, f, nx
            int p = _nodes[f].Prev;
            int nx = _nodes[u].Next;
            _nodes[u].Offset = fo;
            _nodes[f].Offset = fo + us;

            _nodes[u].Prev = p;
            if (p != -1) _nodes[p].Next = u; else _head = u;
            _nodes[u].Next = f;
            _nodes[f].Prev = u;
            _nodes[f].Next = nx;
            if (nx != -1) _nodes[nx].Prev = f; else _tail = f;

            if (nx != -1 && _nodes[nx].Entry == -1)                         // слить f со свободным nx
            {
                _nodes[f].Size += _nodes[nx].Size;
                Unlink(nx);
            }

            movedBytes += req;
            movedBlocks++;
        }
        return new DefragResult(movedBytes, movedBlocks, true);
    }

    // ───────────────────────────── Рост / сжатие ─────────────────────────────

    /// <summary>
    /// Устанавливает ёмкость пула. Уменьшать можно только до конца последнего занятого блока
    /// (сначала вызови Defragment или используй Trim). Недоступно, пока есть Pin.
    /// </summary>
    public void Resize(long newCapacity)
    {
        ThrowIfDisposed();
        newCapacity = AlignUp(Math.Max(newCapacity, 1));
        if (newCapacity == _capacity) return;
        if (_pinTotal > 0)
            throw new InvalidOperationException("Cannot resize the pool while allocations are pinned (base address would change).");
        if (newCapacity > _maxCapacity)
            throw new OutOfMemoryException($"Pool would exceed MaxCapacity ({_maxCapacity} bytes).");

        bool tailFree = _tail != -1 && _nodes[_tail].Entry == -1;
        long usedEnd = tailFree ? _nodes[_tail].Offset : _capacity;
        if (newCapacity < usedEnd)
            throw new InvalidOperationException("Cannot shrink below the last used block. Call Defragment() first, or use Trim().");

        var p = (byte*)NativeMemory.AlignedRealloc(_base, (nuint)newCapacity, (nuint)_alignment);
        if (p == null) throw new OutOfMemoryException();
        _base = p;

        long old = _capacity;
        _capacity = newCapacity;

        if (newCapacity > old)
        {
            if (tailFree) _nodes[_tail].Size += newCapacity - old;
            else InsertAfter(_tail, NewNode(old, newCapacity - old, -1));
        }
        else
        {
            int t = _tail;
            if (newCapacity == _nodes[t].Offset) Unlink(t);
            else _nodes[t].Size = newCapacity - _nodes[t].Offset;
        }
    }

    /// <summary>
    /// Дефрагментирует и уменьшает пул до реально занятого объёма (но не меньше minCapacity).
    /// Если есть закреплённые блоки — только дефрагментирует, размер не трогает.
    /// </summary>
    public long Trim(long minCapacity = 0)
    {
        Defragment();
        if (_pinTotal > 0) return _capacity;
        long usedEnd = (_tail != -1 && _nodes[_tail].Entry == -1) ? _nodes[_tail].Offset : _capacity;
        long target = AlignUp(Math.Max(Math.Max(usedEnd, minCapacity), 1));
        if (target < _capacity) Resize(target);
        return _capacity;
    }

    // ───────────────────────────── Статистика ─────────────────────────────

    public PoolStats GetStats()
    {
        long largest = 0; int freeBlocks = 0;
        for (int n = _head; n != -1; n = _nodes[n].Next)
            if (_nodes[n].Entry == -1) { freeBlocks++; largest = Math.Max(largest, _nodes[n].Size); }
        long free = _capacity - _used;
        double frag = free == 0 ? 0 : 1.0 - (double)largest / free;
        return new PoolStats(_capacity, _used, free, largest, _live, freeBlocks, frag);
    }
}
