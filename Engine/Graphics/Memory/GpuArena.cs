using Engine.Rendering;

namespace Engine.Graphics.Memory;

/// <summary>
/// Общий буфер на GPU, из которого выделяются диапазоны (пул с первым подходящим и слиянием свободных кусков).
/// Работает только через интерфейсы рендера: Diligent здесь не виден.
/// </summary>
public sealed class GpuArena : IDisposable
{
    private readonly IRenderDevice _device;
    private readonly BufferUsage _usage;
    private readonly uint _stride;
    private readonly string _name;
    private readonly List<(int Offset, int Count)> _free = [];

    public IGpuBuffer Buffer { get; private set; } = null!;
    public int Capacity { get; private set; }

    /// <param name="usage">Назначение буфера, например BufferUsage.Vertex или BufferUsage.Index.</param>
    /// <param name="stride">Размер одного элемента в байтах.</param>
    public GpuArena(IRenderDevice device, string name, BufferUsage usage, uint stride, int capacity)
    {
        _device = device;
        _name = name;
        _usage = usage;
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

    /// <summary>Записывает данные в диапазон, начинающийся с элемента offsetInElements (элемент = stride байт).</summary>
    public void Upload<T>(IRenderDevice device, int offsetInElements, ReadOnlySpan<T> data) where T : unmanaged
    {
        if (data.IsEmpty) return;
        device.WriteBuffer(Buffer, (ulong)offsetInElements * _stride, data);
    }

    public void Reset(int newCapacity)
    {
        Buffer.Dispose();
        Create(newCapacity);
    }

    private void Create(int capacity)
    {
        Buffer = _device.CreateBuffer(new GpuBufferDesc(_name, (ulong)capacity * _stride, _usage, BufferAccess.GpuOnly));

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
