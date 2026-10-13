using System.Runtime.InteropServices;
using Engine.Voxels;

namespace Engine.Memory;

public sealed unsafe partial class MemoryPool
{
    // ───────────────────────────── Выделение / освобождение ─────────────────────────────

    public PoolHandle Allocate(long bytes, bool zero = true)
    {
        ThrowIfDisposed();
        if (bytes < 0) throw new ArgumentOutOfRangeException(nameof(bytes));

        long size = AlignUp(Math.Max(bytes, 1));
        int node = AcquireNode(size);
        int e = NewEntry(node, bytes);
        _nodes[node].Entry = e;
        _used += size;
        _live++;

        if (zero) NativeMemory.Clear(_base + _nodes[node].Offset, (nuint)size);
        return new PoolHandle(e, _entries[e].Generation);
    }

    /// <summary>Выделить массив из count элементов T (count = 1 — одиночный объект, например матрица).</summary>
    public PoolHandle<T> Allocate<T>(int count = 1, bool zero = true) where T : unmanaged
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        return new PoolHandle<T>(Allocate((long)count * sizeof(T), zero));
    }

    /// <summary>Выделить и сразу скопировать данные.</summary>
    public PoolHandle<T> AllocateFrom<T>(ReadOnlySpan<T> data) where T : unmanaged
    {
        var h = Allocate<T>(data.Length, zero: false);
        data.CopyTo(GetSpan(h));
        return h;
    }

    public void Free(PoolHandle handle)
    {
        int e = Resolve(handle);
        if (_entries[e].Pins > 0) throw new InvalidOperationException("Cannot free a pinned allocation.");

        int n = _entries[e].Node;
        _used -= _nodes[n].Size;
        _live--;
        _nodes[n].Entry = -1;
        Coalesce(n);

        _entries[e].Node = -1;
        if (++_entries[e].Generation == 0) _entries[e].Generation = 1;
        _entries[e].NextFree = _freeEntry;
        _freeEntry = e;
    }

    /// <summary>Изменить размер аллокации. Handle остаётся тем же, содержимое сохраняется.</summary>
    public void Reallocate(PoolHandle handle, long newBytes, bool zeroNew = true)
    {
        int e = Resolve(handle);
        if (newBytes < 0) throw new ArgumentOutOfRangeException(nameof(newBytes));
        if (_entries[e].Pins > 0) throw new InvalidOperationException("Cannot reallocate a pinned allocation.");

        long oldReq = _entries[e].Requested;
        long newSize = AlignUp(Math.Max(newBytes, 1));
        int node = _entries[e].Node;
        long oldSize = _nodes[node].Size;

        if (newSize < oldSize)
        {
            // сжатие на месте: хвост отдаём обратно в свободные
            long rest = oldSize - newSize;
            int r = NewNode(_nodes[node].Offset + newSize, rest, -1);
            InsertAfter(node, r);
            _nodes[node].Size = newSize;
            _used -= rest;
            Coalesce(r);
        }
        else if (newSize > oldSize)
        {
            long need = newSize - oldSize;
            int nx = _nodes[node].Next;
            if (nx != -1 && _nodes[nx].Entry == -1 && _nodes[nx].Size >= need)
            {
                // рост на месте за счёт свободного соседа
                _nodes[nx].Offset += need;
                _nodes[nx].Size -= need;
                if (_nodes[nx].Size == 0) Unlink(nx);
                _nodes[node].Size = newSize;
                _used += need;
            }
            else
            {
                // переезд в новое место (может сработать дефрагментация/рост — индексы узлов стабильны)
                int nn = AcquireNode(newSize);
                Buffer.MemoryCopy(_base + _nodes[node].Offset, _base + _nodes[nn].Offset, newSize, oldReq);
                _nodes[nn].Entry = e;
                _nodes[node].Entry = -1;
                _entries[e].Node = nn;
                _used += newSize - oldSize;
                Coalesce(node);
                node = nn;
            }
        }

        _entries[e].Requested = newBytes;
        if (zeroNew && newBytes > oldReq)
            NativeMemory.Clear(_base + _nodes[node].Offset + oldReq, (nuint)(newBytes - oldReq));
    }

    public void Reallocate<T>(PoolHandle<T> handle, int newCount, bool zeroNew = true) where T : unmanaged
        => Reallocate(handle.Handle, (long)newCount * sizeof(T), zeroNew);

    // ───────────────────────────── Доступ к данным ─────────────────────────────

    public bool IsAlive(PoolHandle handle)
    {
        if (_base == null || handle.IsNull || (uint)handle.Index >= (uint)_entryCount) return false;
        ref Entry en = ref _entries[handle.Index];
        return en.Generation == handle.Generation && en.Node >= 0;
    }

    public long GetSizeBytes(PoolHandle handle) => _entries[Resolve(handle)].Requested;

    public int GetLength<T>(PoolHandle<T> handle) where T : unmanaged
        => (int)(GetSizeBytes(handle.Handle) / sizeof(T));

    /// <summary>Сырой указатель. Валиден до следующей операции, которая может переместить данные.</summary>
    public byte* GetPointer(PoolHandle handle)
    {
        int e = Resolve(handle);
        return _base + _nodes[_entries[e].Node].Offset;
    }

    public Span<byte> GetBytes(PoolHandle handle)
    {
        int e = Resolve(handle);
        long len = _entries[e].Requested;
        if (len > int.MaxValue) throw new InvalidOperationException("Allocation is too large for a Span.");
        return new Span<byte>(_base + _nodes[_entries[e].Node].Offset, (int)len);
    }

    public Span<T> GetSpan<T>(PoolHandle<T> handle) where T : unmanaged
    {
        int e = Resolve(handle.Handle);
        long count = _entries[e].Requested / sizeof(T);
        if (count > int.MaxValue) throw new InvalidOperationException("Allocation is too large for a Span.");
        return new Span<T>(_base + _nodes[_entries[e].Node].Offset, (int)count);
    }

    /// <summary>Ссылка на первый элемент (для одиночных объектов вроде матрицы).</summary>
    public ref T Get<T>(PoolHandle<T> handle) where T : unmanaged
    {
        int e = Resolve(handle.Handle);
        if (_entries[e].Requested < sizeof(T)) throw new InvalidOperationException("Allocation is empty.");
        return ref *(T*)(_base + _nodes[_entries[e].Node].Offset);
    }
}
