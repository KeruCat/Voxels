using Diligent;
using IDeviceContext = Diligent.IDeviceContext;

namespace Engine.Graphics.Memory;

public sealed unsafe class GpuArena : IDisposable
{
    private readonly IRenderDevice _device;
    private readonly BindFlags _bind;
    private readonly uint _stride;
    private readonly string _name;
    private readonly List<(int Offset, int Count)> _free = [];

    public IBuffer Buffer { get; private set; } = null!;
    public int Capacity { get; private set; }

    public GpuArena(IRenderDevice device, string name, BindFlags bind, uint stride, int capacity)
    {
        _device = device;
        _name = name;
        _bind = bind;
        _stride = stride;
        Create(capacity);
    }

    public int Allocate(int count)
    {
        if (count <= 0) return 0;

        for (int i = 0; i < _free.Count; i++)
        {
            var (offset, size) = _free[i];
            if (size < count) continue;

            if (size == count) _free.RemoveAt(i);
            else _free[i] = (offset + count, size - count);
            return offset;
        }
        return -1;
    }

    public void Free(int offset, int count)
    {
        if (count <= 0 || offset < 0) return;

        int i = 0;
        while (i < _free.Count && _free[i].Offset < offset) i++;
        _free.Insert(i, (offset, count));

        if (i + 1 < _free.Count && _free[i].Offset + _free[i].Count == _free[i + 1].Offset)
        {
            _free[i] = (_free[i].Offset, _free[i].Count + _free[i + 1].Count);
            _free.RemoveAt(i + 1);
        }
        if (i > 0 && _free[i - 1].Offset + _free[i - 1].Count == _free[i].Offset)
        {
            _free[i - 1] = (_free[i - 1].Offset, _free[i - 1].Count + _free[i].Count);
            _free.RemoveAt(i);
        }
    }

    public void Upload<T>(IDeviceContext context, int offsetInElements, ReadOnlySpan<T> data) where T : unmanaged
    {
        if (data.IsEmpty) return;

        fixed (T* pointer = data)
        {
            context.UpdateBuffer(
                Buffer,
                (ulong)offsetInElements * _stride,
                (ulong)data.Length * (ulong)sizeof(T),
                (IntPtr)pointer,
                ResourceStateTransitionMode.Transition);
        }
    }

    public void Reset(int newCapacity)
    {
        Buffer.Dispose();   
        Create(newCapacity);
    }

    private void Create(int capacity)
    {
        Buffer = _device.CreateBuffer(new()
        {
            Name = _name,
            Usage = Usage.Default,
            BindFlags = _bind,
            Size = (ulong)capacity * _stride
        });

        Capacity = capacity;
        _free.Clear();
        _free.Add((0, capacity));
    }
    public ArenaStats GetStats()
    {
        int free = 0, largest = 0;
        foreach (var (_, count) in _free)
        {
            free += count;
            largest = Math.Max(largest, count);
        }
        double frag = free == 0 ? 0 : 1.0 - (double)largest / free;
        return new ArenaStats(Capacity, Capacity - free, free, largest, _free.Count, frag, _stride);
    }
    public void Dispose() => Buffer.Dispose();
}
