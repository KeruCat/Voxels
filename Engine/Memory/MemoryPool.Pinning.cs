namespace Engine.Memory;

public sealed unsafe partial class MemoryPool
{
    // ───────────────────────────── Pin ─────────────────────────────

    /// <summary>
    /// Закрепляет аллокацию: дефрагментация её не двигает, Free/Reallocate запрещены,
    /// а пока есть хоть один pin — пул не может расти/сжиматься (адрес базы не меняется).
    /// </summary>
    public PinScope Pin(PoolHandle handle)
    {
        int e = Resolve(handle);
        _entries[e].Pins++;
        _pinTotal++;
        return new PinScope(this, handle, _base + _nodes[_entries[e].Node].Offset);
    }

    private void Unpin(PoolHandle handle)
    {
        int e = Resolve(handle);
        _entries[e].Pins--;
        _pinTotal--;
    }

    public readonly struct PinScope : IDisposable
    {
        private readonly MemoryPool _pool;
        private readonly PoolHandle _handle;
        public readonly byte* Pointer;

        internal PinScope(MemoryPool pool, PoolHandle handle, byte* ptr)
        { _pool = pool; _handle = handle; Pointer = ptr; }

        public void Dispose() => _pool?.Unpin(_handle);
    }
}
