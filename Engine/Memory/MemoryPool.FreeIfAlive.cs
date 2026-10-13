namespace Engine.Memory;

public sealed unsafe partial class MemoryPool
{
    /// <summary>Освобождает блок, если handle жив, и обнуляет handle.</summary>
    public void FreeIfAlive(ref PoolHandle h)
    {
        if (IsAlive(h)) Free(h);
        h = default;
    }

    public void FreeIfAlive<T>(ref PoolHandle<T> h) where T : unmanaged
    {
        if (IsAlive(h.Handle)) Free(h.Handle);
        h = default;
    }
}
