using System.Runtime.InteropServices;

namespace Engine.Memory;

public sealed unsafe partial class MemoryPool
{
    // ───────────────────────────── Внутренности ─────────────────────────────

    private int AcquireNode(long size)
    {
        int n = TryAllocNode(size);
        if (n >= 0) return n;

        if (AutoDefragment && FreeBytes >= size)
        {
            Defragment();
            n = TryAllocNode(size);
            if (n >= 0) return n;
        }

        if (AutoGrow)
        {
            long tailFree = (_tail != -1 && _nodes[_tail].Entry == -1) ? _nodes[_tail].Size : 0;
            long target = Math.Max(_capacity + (size - tailFree), (long)(_capacity * GrowthFactor));
            target = Math.Min(AlignUp(target), _maxCapacity);
            if (target - _capacity + tailFree >= size)
            {
                Resize(target);
                n = TryAllocNode(size);
                if (n >= 0) return n;
            }
        }
        throw new OutOfMemoryException($"Pool cannot satisfy allocation of {size} bytes (capacity {_capacity}, free {FreeBytes}).");
    }

    // best-fit по списку; возвращает узел, у которого Size == size (остаток отрезан в новый свободный узел)
    private int TryAllocNode(long size)
    {
        int best = -1;
        for (int n = _head; n != -1; n = _nodes[n].Next)
        {
            if (_nodes[n].Entry != -1 || _nodes[n].Size < size) continue;
            if (best == -1 || _nodes[n].Size < _nodes[best].Size) best = n;
            if (_nodes[n].Size == size) break;
        }
        if (best == -1) return -1;

        long rest = _nodes[best].Size - size;
        if (rest > 0)
        {
            int r = NewNode(_nodes[best].Offset + size, rest, -1);
            InsertAfter(best, r);
            _nodes[best].Size = size;
        }
        return best;
    }

    private void Coalesce(int n)
    {
        int nx = _nodes[n].Next;
        if (nx != -1 && _nodes[nx].Entry == -1)
        {
            _nodes[n].Size += _nodes[nx].Size;
            Unlink(nx);
        }
        int pv = _nodes[n].Prev;
        if (pv != -1 && _nodes[pv].Entry == -1)
        {
            _nodes[pv].Size += _nodes[n].Size;
            Unlink(n);
        }
    }

    private int NewNode(long offset, long size, int entry)
    {
        int i = _freeNodes.Count > 0 ? _freeNodes.Pop() : _nodeCount++;
        if (i >= _nodes.Length) Array.Resize(ref _nodes, _nodes.Length * 2);
        _nodes[i] = new Node { Offset = offset, Size = size, Prev = -1, Next = -1, Entry = entry };
        return i;
    }

    private void InsertAfter(int after, int node)
    {
        int nx = _nodes[after].Next;
        _nodes[node].Prev = after;
        _nodes[node].Next = nx;
        _nodes[after].Next = node;
        if (nx != -1) _nodes[nx].Prev = node; else _tail = node;
    }

    private void Unlink(int n)
    {
        int p = _nodes[n].Prev, nx = _nodes[n].Next;
        if (p != -1) _nodes[p].Next = nx; else _head = nx;
        if (nx != -1) _nodes[nx].Prev = p; else _tail = p;
        _freeNodes.Push(n);
    }

    private int NewEntry(int node, long requested)
    {
        int e;
        if (_freeEntry != -1)
        {
            e = _freeEntry;
            _freeEntry = _entries[e].NextFree;
        }
        else
        {
            e = _entryCount++;
            if (e >= _entries.Length) Array.Resize(ref _entries, _entries.Length * 2);
            _entries[e].Generation = 1;
        }
        _entries[e].Node = node;
        _entries[e].Pins = 0;
        _entries[e].NextFree = -1;
        _entries[e].Requested = requested;
        return e;
    }

    private int Resolve(PoolHandle h)
    {
        ThrowIfDisposed();
        if (h.IsNull || (uint)h.Index >= (uint)_entryCount ||
            _entries[h.Index].Generation != h.Generation || _entries[h.Index].Node < 0)
            throw new ArgumentException("Invalid or stale PoolHandle (already freed?).");
        return h.Index;
    }

    private long AlignUp(long v) => (v + _alignment - 1) & ~(long)(_alignment - 1);

    private void ThrowIfDisposed()
    {
        if (_base == null) throw new ObjectDisposedException(nameof(MemoryPool));
    }

    private void Release()
    {
        if (_base != null) { NativeMemory.AlignedFree(_base); _base = null; }
    }

    public void Dispose() { Release(); GC.SuppressFinalize(this); }
    ~MemoryPool() => Release();
}
