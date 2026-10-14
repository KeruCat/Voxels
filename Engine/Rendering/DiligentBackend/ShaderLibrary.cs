using Diligent;
using Engine.Graphics;

namespace Engine.Rendering.DiligentBackend;

/// <summary>Загрузка скомпилированных шейдеров Diligent по описаниям ShaderInfo. Внутренний класс бэкенда.</summary>
internal sealed class ShaderLibrary : IDisposable
{
    private readonly Diligent.IRenderDevice _device;
    private readonly IShaderSourceInputStreamFactory _sources;

    internal ShaderLibrary(string rootDir, GraphicsContext gfx)
    {
        _device = gfx.Device!;
        _sources = gfx.Factory!.CreateDefaultShaderSourceStreamFactory(rootDir);
    }

    internal IShader Create(ShaderInfo info)
        => _device.CreateShader(new()
        {
            FilePath = info.Path,
            ShaderSourceStreamFactory = _sources,
            EntryPoint = info.Entry,
            Desc = new() { Name = info.Name, ShaderType = ToDiligent(info.Type), UseCombinedTextureSamplers = true },
            SourceLanguage = ShaderSourceLanguage.Hlsl
        }, out _) ?? throw new Exception(
            $"Shader creation failed: {info.Name} ({info.Path}, entry {info.Entry}).");

    private static ShaderType ToDiligent(ShaderStage stage) => stage switch
    {
        ShaderStage.Vertex => ShaderType.Vertex,
        ShaderStage.Pixel => ShaderType.Pixel,
        _ => throw new ArgumentOutOfRangeException(nameof(stage)),
    };

    public void Dispose() => _sources.Dispose();
}