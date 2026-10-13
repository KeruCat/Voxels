namespace Engine.Graphics;

/// <summary>Место меша в общих GPU-арены (в элементах). IndexCount == 0 — на GPU ничего нет.</summary>
public readonly record struct GpuRange(int VertexOffset, int IndexOffset, int VertexCount, int IndexCount);
