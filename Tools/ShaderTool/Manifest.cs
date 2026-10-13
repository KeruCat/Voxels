using System.Text.Json;

namespace ShaderTool;

public enum ShaderStage { Vertex, Fragment }

public static class ShaderStages
{
    public static ShaderStage Parse(string text) => text.Trim().ToLowerInvariant() switch
    {
        "vertex" or "vs" => ShaderStage.Vertex,
        "fragment" or "pixel" or "ps" => ShaderStage.Fragment,
        _ => throw new ShaderToolException($"unknown stage '{text}' (use vertex or fragment)")
    };

    /// <summary>Расширение выходного файла (как в примерах Diligent).</summary>
    public static string Extension(this ShaderStage stage) => stage == ShaderStage.Vertex ? "vsh" : "psh";

    /// <summary>Короткая метка для отладочного имени: "Voxel VS".</summary>
    public static string Label(this ShaderStage stage) => stage == ShaderStage.Vertex ? "VS" : "PS";

    /// <summary>Имя члена перечисления Diligent.ShaderType.</summary>
    public static string DiligentType(this ShaderStage stage) => stage == ShaderStage.Vertex ? "Vertex" : "Pixel";
}

/// <summary>Одна точка входа и её стадия: из {"vertex": "VSMain"}.</summary>
public sealed record StageEntry(ShaderStage Stage, string Entry);

/// <summary>Один .slang и то, что из него нужно получить.</summary>
public sealed record ShaderDef(string Source, string? Name, string Dir, IReadOnlyList<StageEntry> Output);

public sealed record CatalogOptions(string File, string Namespace);

/// <summary>Содержимое shaders.json. Все пути относительны папке, где лежит сам манифест.</summary>
public sealed record Manifest(
    IReadOnlyList<string> Include,
    string OutputDir,
    string? RuntimePrefix,
    CatalogOptions? Catalog,
    IReadOnlyList<ShaderDef> Shaders)
{
    public static Manifest Load(string path)
    {
        if (!File.Exists(path))
            throw new ShaderToolException($"manifest not found: {path}");

        var options = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
        using var doc = JsonDocument.Parse(File.ReadAllText(path), options);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new ShaderToolException("manifest root must be an object");

        var include = TryGet(root, "include", out var inc)
            ? ReadStringArray(inc, "include")
            : ["."];

        string outputDir = TryGet(root, "outputDir", out var od) ? ReadString(od, "outputDir") : ".";
        string? runtimePrefix = TryGet(root, "runtimePrefix", out var rp) ? ReadString(rp, "runtimePrefix") : null;

        CatalogOptions? catalog = null;
        if (TryGet(root, "catalog", out var cat))
        {
            if (cat.ValueKind != JsonValueKind.Object || !TryGet(cat, "file", out var file))
                throw new ShaderToolException("catalog must be an object with a 'file' property");
            string ns = TryGet(cat, "namespace", out var n) ? ReadString(n, "catalog.namespace") : "GameApp";
            catalog = new CatalogOptions(ReadString(file, "catalog.file"), ns);
        }

        if (!TryGet(root, "shaders", out var shadersElement) || shadersElement.ValueKind != JsonValueKind.Array)
            throw new ShaderToolException("manifest needs a 'shaders' array");

        var shaders = new List<ShaderDef>();
        foreach (var item in shadersElement.EnumerateArray())
            shaders.Add(ReadShader(item, shaders.Count));
        if (shaders.Count == 0)
            throw new ShaderToolException("manifest has no shaders");

        return new Manifest(include, outputDir, runtimePrefix, catalog, shaders);
    }

    private static ShaderDef ReadShader(JsonElement item, int index)
    {
        string where = $"shaders[{index}]";
        if (item.ValueKind != JsonValueKind.Object)
            throw new ShaderToolException($"{where} must be an object");
        if (!TryGet(item, "source", out var source))
            throw new ShaderToolException($"{where}: 'source' is required");

        string? name = TryGet(item, "name", out var nameElement) ? ReadString(nameElement, $"{where}.name") : null;
        string dir = TryGet(item, "dir", out var dirElement) ? ReadString(dirElement, $"{where}.dir") : "";

        if (!TryGet(item, "output", out var output) || output.ValueKind != JsonValueKind.Array)
            throw new ShaderToolException($"{where}: 'output' must be an array like [{{\"vertex\": \"VSMain\"}}, {{\"fragment\": \"FSMain\"}}]");

        var entries = new List<StageEntry>();
        foreach (var e in output.EnumerateArray())
        {
            var props = e.ValueKind == JsonValueKind.Object ? e.EnumerateObject().ToList() : [];
            if (props.Count != 1 || props[0].Value.ValueKind != JsonValueKind.String)
                throw new ShaderToolException($"{where}.output: every item must look like {{\"vertex\": \"EntryName\"}}");

            var entry = new StageEntry(ShaderStages.Parse(props[0].Name), props[0].Value.GetString()!);
            if (entries.Contains(entry))
                throw new ShaderToolException($"{where}.output: duplicate {props[0].Name} entry '{entry.Entry}'");
            entries.Add(entry);
        }
        if (entries.Count == 0)
            throw new ShaderToolException($"{where}.output is empty");

        return new ShaderDef(ReadString(source, $"{where}.source"), name, dir, entries);
    }

    private static bool TryGet(JsonElement obj, string name, out JsonElement value)
    {
        foreach (var property in obj.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }
        value = default;
        return false;
    }

    private static string ReadString(JsonElement e, string what)
        => e.ValueKind == JsonValueKind.String
            ? e.GetString()!
            : throw new ShaderToolException($"'{what}' must be a string");

    private static List<string> ReadStringArray(JsonElement e, string what)
    {
        if (e.ValueKind != JsonValueKind.Array)
            throw new ShaderToolException($"'{what}' must be an array of strings");
        return e.EnumerateArray().Select(x => ReadString(x, what)).ToList();
    }
}

public sealed class ShaderToolException(string message) : Exception(message);