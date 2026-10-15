using System.Numerics;
using System.Runtime.InteropServices;
using Engine.Memory;
using Game.Voxels;

namespace Game.World;

/// <summary>Лёгкие метаданные одного вокселя: 6 байт.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct VoxelMeta
{
    /// <summary>Малое значение, которое помещается в вокселе (вид, уровень, цвет и т.п.).</summary>
    public ushort Kind;
    /// <summary>Ссылка в ComponentStore (0 — нет). Сундук, табличка и т.п.</summary>
    public uint Ref;
}

/// <summary>
/// Разреженное хранилище метаданных чанка. Ничего не выделяется, пока метаданных нет.
/// Mask — бит на воксель (4096 бит); Prefix[w] — сколько записей в словах до w; Entries — записи по возрастанию индекса.
/// Позиция записи вокселя = Prefix[w] + popcount(Mask[w] ниже бита).
/// </summary>
public struct MetaSlot
{
    public PoolHandle<ulong> Mask;
    public PoolHandle<ushort> Prefix;
    public PoolHandle<VoxelMeta> Entries;
    public int Count;
    public int Capacity;
}

public static class ChunkMeta
{
    private const int Words = Vox.Volume / 64;

    public static bool Has(MemoryPool pool, in MetaSlot slot, int index)
    {
        if (slot.Count == 0) return false;
        var mask = pool.GetSpan(slot.Mask);
        return (mask[index >> 6] & (1UL << (index & 63))) != 0;
    }

    public static bool TryGet(MemoryPool pool, in MetaSlot slot, int index, out VoxelMeta meta)
    {
        meta = default;
        if (!Has(pool, slot, index)) return false;
        meta = pool.GetSpan(slot.Entries)[Rank(pool, slot, index)];
        return true;
    }

    /// <summary>Записывает метаданные вокселя (создаёт или заменяет запись).</summary>
    public static void Set(MemoryPool pool, ref MetaSlot slot, int index, VoxelMeta meta)
    {
        if (!pool.IsAlive(slot.Mask))
        {
            slot.Mask = pool.AllocateFrom<ulong>(new ulong[Words]);
            slot.Prefix = pool.AllocateFrom<ushort>(new ushort[Words + 1]);
        }

        if (Has(pool, slot, index))
        {
            pool.GetSpan(slot.Entries)[Rank(pool, slot, index)] = meta;
            return;
        }

        if (slot.Count + 1 > slot.Capacity) Grow(pool, ref slot, Math.Max(4, slot.Capacity * 2));

        int r = Rank(pool, slot, index);
        var entries = pool.GetSpan(slot.Entries);
        entries.Slice(r, slot.Count - r).CopyTo(entries.Slice(r + 1));   // сдвиг вправо; CopyTo безопасен для перекрытия
        entries[r] = meta;
        slot.Count++;

        var mask = pool.GetSpan(slot.Mask);
        mask[index >> 6] |= 1UL << (index & 63);
        RebuildPrefix(pool, slot);
    }

    /// <summary>Удаляет метаданные вокселя. Если записей не осталось — освобождает все блоки.</summary>
    public static bool Remove(MemoryPool pool, ref MetaSlot slot, int index)
    {
        if (!Has(pool, slot, index)) return false;

        int r = Rank(pool, slot, index);
        var entries = pool.GetSpan(slot.Entries);
        entries.Slice(r + 1, slot.Count - r - 1).CopyTo(entries.Slice(r));   // сдвиг влево
        slot.Count--;

        var mask = pool.GetSpan(slot.Mask);
        mask[index >> 6] &= ~(1UL << (index & 63));

        if (slot.Count == 0) Free(pool, ref slot);
        else RebuildPrefix(pool, slot);
        return true;
    }

    /// <summary>Освобождает всё хранилище чанка (например, при выгрузке или когда чанк стал воздухом).</summary>
    public static void Free(MemoryPool pool, ref MetaSlot slot)
    {
        pool.FreeIfAlive(ref slot.Mask);
        pool.FreeIfAlive(ref slot.Prefix);
        pool.FreeIfAlive(ref slot.Entries);
        slot.Count = 0;
        slot.Capacity = 0;
    }

    private static int Rank(MemoryPool pool, in MetaSlot slot, int index)
    {
        int w = index >> 6, bit = index & 63;
        ulong below = pool.GetSpan(slot.Mask)[w] & ((1UL << bit) - 1);
        return pool.GetSpan(slot.Prefix)[w] + BitOperations.PopCount(below);
    }

    private static void RebuildPrefix(MemoryPool pool, in MetaSlot slot)
    {
        var mask = pool.GetSpan(slot.Mask);
        var prefix = pool.GetSpan(slot.Prefix);
        int acc = 0;
        for (int w = 0; w < Words; w++)
        {
            prefix[w] = (ushort)acc;
            acc += BitOperations.PopCount(mask[w]);
        }
        prefix[Words] = (ushort)acc;
    }

    private static void Grow(MemoryPool pool, ref MetaSlot slot, int newCapacity)
    {
        var fresh = new VoxelMeta[newCapacity];
        if (slot.Count > 0)
            pool.GetSpan(slot.Entries).Slice(0, slot.Count).CopyTo(fresh);
        var old = slot.Entries;
        slot.Entries = pool.AllocateFrom<VoxelMeta>(fresh);
        pool.FreeIfAlive(ref old);
        slot.Capacity = newCapacity;
    }
}
