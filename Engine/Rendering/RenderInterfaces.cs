using System.Numerics;

namespace Engine.Rendering;

/// <summary>Буфер на GPU. Размер фиксирован при создании.</summary>
public interface IGpuBuffer : IDisposable
{
    string Name { get; }
    ulong Size { get; }
}

/// <summary>Двумерная текстура (только для чтения в шейдере).</summary>
public interface ITexture2D : IDisposable
{
    string Name { get; }
    int Width { get; }
    int Height { get; }
}

/// <summary>Готовый пайплайн (шейдеры + состояние). Создаётся один раз, используется много раз.</summary>
public interface IPipeline : IDisposable
{
    string Name { get; }
}

/// <summary>
/// Команды текущего кадра: всё, что раньше делалось напрямую через контекст Diligent.
/// Игра пишет команды только через этот интерфейс.
/// </summary>
public interface IRenderCommands
{
    void SetPipeline(IPipeline pipeline);
    void SetVertexBuffer(int slot, IGpuBuffer buffer, ulong offset = 0);
    void SetIndexBuffer(IGpuBuffer buffer, IndexFormat format, ulong offset = 0);
    void SetTexture(string name, ITexture2D texture);

    /// <summary>Записывает значение в константный буфер шейдера с именем name.</summary>
    void SetConstants<T>(string name, in T value) where T : unmanaged;

    /// <summary>Записывает данные в буфер со смещения в байтах (GpuOnly — копированием, CpuWrite — через отображение).</summary>
    void Write<T>(IGpuBuffer buffer, ulong byteOffset, ReadOnlySpan<T> data) where T : unmanaged;

    /// <summary>Отрисовка без индексов.</summary>
    void Draw(in DrawDesc draw);

    /// <summary>Отрисовка с индексами, один вызов.</summary>
    void DrawIndexed(in DrawIndexedCommand command);

    /// <summary>
    /// Один вызов на несколько команд из буфера аргументов (multi-draw indirect).
    /// args — буфер с BufferUsage.Indirect, offset — смещение первой команды в байтах.
    /// </summary>
    void DrawIndexedIndirect(IGpuBuffer args, ulong offset, uint drawCount, uint stride);
}

/// <summary>Устройство рендера: создание ресурсов, кадр, вывод на экран.</summary>
public interface IRenderDevice : IDisposable
{
    /// <summary>Есть ли у устройства multi-draw indirect (один вызов на много команд).</summary>
    bool SupportsMultiDrawIndirect { get; }

    IGpuBuffer CreateBuffer(GpuBufferDesc desc);
    ITexture2D CreateTexture(TextureDesc desc, ReadOnlySpan<byte> rgbaPixels);
    IPipeline CreatePipeline(PipelineDesc desc);

    /// <summary>Записывает данные в буфер вне кадра (например, загрузка мешей во время обновления мира).</summary>
    void WriteBuffer<T>(IGpuBuffer buffer, ulong byteOffset, ReadOnlySpan<T> data) where T : unmanaged;

    /// <summary>Начинает кадр и очищает цвет. Команды записываются в Commands до Present.</summary>
    IRenderCommands BeginFrame(Vector4 clearColor);

    /// <summary>Отправляет записанный кадр на экран.</summary>
    void Present();

    void Resize(uint width, uint height);
}
