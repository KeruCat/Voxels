using Diligent;
using Engine.Graphics.Shaders.Generated;

namespace Engine.Graphics.Shaders;
internal sealed class ShaderLibrary : IDisposable
{
    private readonly IRenderDevice _device;
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
            Desc = new() { Name = info.Name, ShaderType = info.Type, UseCombinedTextureSamplers = true },
            SourceLanguage = ShaderSourceLanguage.Hlsl
        }, out _) ?? throw new Exception(
            $"Shader creation failed: {info.Name} ({info.Path}, entry {info.Entry}).");

    public void Dispose() => _sources.Dispose();
}
