namespace Engine.Rendering;

// Простые типы рендера, не зависящие от конкретного API (Diligent сейчас, другой бэкенд потом).

[Flags]
public enum BufferUsage
{
    None = 0,
    Vertex = 1,
    Index = 2,
    Indirect = 4,    // аргументы DrawIndexedIndirect
    Constant = 8,
}

/// <summary>GpuOnly — обновляется через Write (копирование); CpuWrite — часто меняется с CPU (отображение памяти).</summary>
public enum BufferAccess { GpuOnly, CpuWrite }

public enum IndexFormat { UInt16, UInt32 }

public enum TextureFormat { Rgba8 }

public enum VertexFormat
{
    UInt8x4,
    UInt32,
    Float32x2,
    Float32x3,
    Float32x4,
}

public enum SamplerFilter { Point, Linear }

public enum BlendMode { Opaque, AlphaBlend }

public readonly record struct GpuBufferDesc(string Name, ulong Size, BufferUsage Usage, BufferAccess Access);

public readonly record struct TextureDesc(string Name, int Width, int Height, TextureFormat Format);

/// <summary>Один вход вершинного буфера: Location — номер атрибута в шейдере (ATTRIB{Location}).</summary>
public readonly record struct VertexInput(int Location, int BufferSlot, VertexFormat Format, bool PerInstance = false);

/// <summary>
/// Описание пайплайна: шейдеры по имени (без расширений и суффиксов), входы вершин, состояние смешивания
/// и глубины, а также текстуры, которые шейдер читает через статический сэмплер.
/// </summary>
public sealed class PipelineDesc
{
    public required string Name { get; init; }
    public required string VertexShader { get; init; }
    public required string PixelShader { get; init; }
    public required VertexInput[] Inputs { get; init; }
    public BlendMode Blend { get; init; } = BlendMode.Opaque;
    public bool DepthTest { get; init; } = true;
    public bool DepthWrite { get; init; } = true;
    public bool CounterClockwiseFront { get; init; } = true;
    /// <summary>Имена текстур, которые шейдер читает пиксельным сэмплером; сэмплер задаётся ниже.</summary>
    public string[] PixelTextures { get; init; } = [];
    /// <summary>Имена константных буферов (вершинная стадия). Значения задаёт SetConstants с тем же именем.</summary>
    public string[] ConstantBuffers { get; init; } = [];
    public SamplerFilter TextureFilter { get; init; } = SamplerFilter.Point;
}

/// <summary>Невызываемая команда отрисовки одного вызова без индексов.</summary>
public readonly record struct DrawDesc(uint VertexCount, uint InstanceCount, uint FirstVertex, uint FirstInstance);

/// <summary>
/// Команда отрисовки с индексами. Раскладка совпадает с аргументами DrawIndexedIndirect (20 байт),
/// поэтому массив таких команд можно отдать GPU как есть.
/// </summary>
public struct DrawIndexedCommand
{
    public uint IndexCount;
    public uint InstanceCount;
    public uint FirstIndex;
    public int BaseVertex;
    public uint FirstInstance;
}
