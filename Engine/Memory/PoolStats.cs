namespace Engine.Memory;

public readonly record struct DefragResult(long BytesMoved, int BlocksMoved, bool Completed);

public readonly record struct PoolStats(
    long Capacity, long Used, long Free, long LargestFreeBlock,
    int LiveAllocations, int FreeBlocks, double Fragmentation);
