using Diligent;

namespace Engine.Rendering.DiligentBackend;

// Обёртки ресурсов Diligent под интерфейсы Engine.Rendering. Наружу Diligent не выходит.

internal sealed class DiligentBuffer : IGpuBuffer
{
    public DiligentBuffer(string name, ulong size, BufferAccess access, IBuffer native)
    {
        Name = name; Size = size; Access = access; Native = native;
    }

    public string Name { get; }
    public ulong Size { get; }
    public BufferAccess Access { get; }
    internal IBuffer Native { get; }

    public void Dispose() => Native.Dispose();
}

internal sealed class DiligentTexture : ITexture2D
{
    public DiligentTexture(string name, int width, int height, ITexture native)
    {
        Name = name; Width = width; Height = height; Native = native;
    }

    public string Name { get; }
    public int Width { get; }
    public int Height { get; }
    internal ITexture Native { get; }

    public void Dispose() => Native.Dispose();
}

internal sealed class DiligentPipeline : IPipeline
{
    public DiligentPipeline(PipelineDesc desc, IPipelineState native, IShaderResourceBinding srb)
    {
        Desc = desc; Native = native; Srb = srb;
    }

    public string Name => Desc.Name;
    public PipelineDesc Desc { get; }
    internal IPipelineState Native { get; }
    internal IShaderResourceBinding Srb { get; }

    public void Dispose()
    {
        Srb.Dispose();
        Native.Dispose();
    }
}
