namespace Engine.Rendering;

/// <summary>Стадия шейдера. Нейтральна: бэкенд сам сопоставляет её с API.</summary>
public enum ShaderStage { Vertex, Pixel }

/// <param name="Name">debug name.</param>
/// <param name="Path">Path for ShaderSourceStreamFactory (relative to the application folder).</param>
/// <param name="Entry">Name of the entry point in HLSL.</param>
/// <param name="Type">Shader stage.</param>
/// <param name="Source">Original .slang relative to shaders.json.</param>
public sealed record ShaderInfo(string Name, string Path, string Entry, ShaderStage Type, string Source);