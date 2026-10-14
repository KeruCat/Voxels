namespace Engine.Graphics;

/// <summary>
/// Граница движок ↔ игра: игра отдаёт готовые байты меша, движок решает, куда их положить на GPU.
/// Игра не знает ни про буферы, ни про контекст устройства.
/// </summary>
public interface IMeshSink
{
    /// <summary>false — нет места в аренах, повторить в следующем кадре.</summary>
    bool TryUpload(ReadOnlySpan<byte> vertices, int vertexCount, ReadOnlySpan<byte> indices, int indexCount, out GpuRange range);

    void Release(GpuRange range);
}
