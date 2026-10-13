namespace Engine.Graphics.Memory;

public readonly record struct ArenaStats(
    int Capacity, int Used, int Free, int LargestFree, int FreeRanges, double Fragmentation, uint Stride);
