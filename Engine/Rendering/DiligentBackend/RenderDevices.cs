using Engine.Graphics;

namespace Engine.Rendering.DiligentBackend;

/// <summary>Точка создания устройства рендера. Игра получает только IRenderDevice.</summary>
public static class RenderDevices
{
    internal static IRenderDevice CreateDiligent(GraphicsContext gfx, string shaderRoot, IReadOnlyDictionary<string, ShaderInfo> shaders) =>
        new DiligentRenderDevice(gfx, shaderRoot, shaders);
}
