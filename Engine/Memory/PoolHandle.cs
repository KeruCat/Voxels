namespace Engine.Memory;

public readonly struct PoolHandle : IEquatable<PoolHandle>
{
    internal readonly int Index;
    internal readonly int Generation;

    internal PoolHandle(int index, int generation) { Index = index; Generation = generation; }

    public static PoolHandle Null => default;
    public bool IsNull => Generation == 0;

    /// <summary>Ключ одним числом .</summary>
    public ulong Key => ((ulong)(uint)Generation << 32) | (uint)Index;

    public bool Equals(PoolHandle o) => Index == o.Index && Generation == o.Generation;
    public override bool Equals(object? o) => o is PoolHandle h && Equals(h);
    public override int GetHashCode() => HashCode.Combine(Index, Generation);
    public static bool operator ==(PoolHandle a, PoolHandle b) => a.Equals(b);
    public static bool operator !=(PoolHandle a, PoolHandle b) => !a.Equals(b);
    public override string ToString() => IsNull ? "PoolHandle(null)" : $"PoolHandle({Index}:{Generation})";
}

public readonly struct PoolHandle<T> : IEquatable<PoolHandle<T>> where T : unmanaged
{
    public readonly PoolHandle Handle;
    internal PoolHandle(PoolHandle h) { Handle = h; }

    public bool IsNull => Handle.IsNull;
    public ulong Key => Handle.Key;

    public static implicit operator PoolHandle(PoolHandle<T> h) => h.Handle;
    public bool Equals(PoolHandle<T> o) => Handle == o.Handle;
    public override bool Equals(object? o) => o is PoolHandle<T> h && Equals(h);
    public override int GetHashCode() => Handle.GetHashCode();
    public override string ToString() => $"{typeof(T).Name}:{Handle}";
}
