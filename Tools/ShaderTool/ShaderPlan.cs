using System.Text;

namespace ShaderTool;

/// <summary>Одна точка входа, у которой уже вычислено всё: пути, имена, члены каталога.</summary>
public sealed record PlannedShader(
    string SourcePath,        // абсолютный путь к .slang
    string SourceRelative,    // относительно манифеста, для подписей
    string Entry,
    ShaderStage Stage,
    string OutputPath,        // абсолютный путь выходного HLSL
    string RuntimePath,       // путь для Diligent (ShaderSourceStreamFactory), через '/'
    string DebugName,         // "Voxel VS": имя шейдера для логов Diligent и RenderDoc
    string ClassName,         // "Voxel": класс в ShaderCatalog
    string MemberName);       // "Vertex" или "Vertex_VSShadow"

public sealed record ShaderPlan(
    string BaseDir,
    string ManifestPath,
    IReadOnlyList<string> IncludeDirs,
    IReadOnlyList<PlannedShader> Entries,
    string? CatalogFile,
    string CatalogNamespace);

/// <summary>Превращает манифест в конкретный план: что, откуда и куда собирать, и как это назвать.</summary>
public static class ShaderPlanner
{
    public static ShaderPlan Create(Manifest manifest, string manifestPath)
    {
        string manifestFull = Path.GetFullPath(manifestPath);
        string baseDir = Path.GetDirectoryName(manifestFull)!;

        var includeDirs = manifest.Include
            .Select(dir => Path.GetFullPath(dir, baseDir))
            .Where(Directory.Exists)
            .ToList();
        if (includeDirs.Count == 0)
            includeDirs.Add(baseDir);

        // Путь внутри собранного приложения: папка с манифестом копируется как <prefix>/…
        string prefix = manifest.RuntimePrefix
                        ?? Path.GetFileName(baseDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        var entries = new List<PlannedShader>();
        var classNames = new Dictionary<string, string>();                 // класс → source (поиск дублей)
        var outputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var def in manifest.Shaders)
        {
            string stem = Path.GetFileNameWithoutExtension(def.Source);
            string display = def.Name ?? ToPascal(stem);
            string className = ToPascal(display);

            if (classNames.TryGetValue(className, out string? other))
                throw new ShaderToolException(
                    $"'{def.Source}' and '{other}' both map to ShaderCatalog.{className}. Set a distinct \"name\" for one of them.");
            classNames[className] = def.Source;

            foreach (var group in def.Output.GroupBy(e => e.Stage))
            {
                bool several = group.Count() > 1;   // две и более точки входа одной стадии → имена по точке входа
                foreach (var e in group)
                {
                    string fileName = several ? $"{stem}.{e.Entry}.{e.Stage.Extension()}" : $"{stem}.{e.Stage.Extension()}";
                    string outputPath = Path.GetFullPath(Path.Combine(manifest.OutputDir, def.Dir, fileName), baseDir);

                    string relative = Path.GetRelativePath(baseDir, outputPath);
                    if (relative.StartsWith("..") || Path.IsPathRooted(relative))
                        throw new ShaderToolException(
                            $"output '{outputPath}' is outside the manifest folder; it must stay inside so it can be copied to the app as '{prefix}/…'");

                    if (outputs.TryGetValue(outputPath, out string? clash))
                        throw new ShaderToolException($"'{clash}' and '{def.Source}' produce the same file '{relative}'");
                    outputs[outputPath] = def.Source;

                    string runtime = relative.Replace('\\', '/');
                    if (prefix.Length > 0)
                        runtime = prefix.Trim('/') + "/" + runtime;

                    string label = $"{display} {e.Stage.Label()}";
                    entries.Add(new PlannedShader(
                        SourcePath: Path.GetFullPath(def.Source, baseDir),
                        SourceRelative: def.Source.Replace('\\', '/'),
                        Entry: e.Entry,
                        Stage: e.Stage,
                        OutputPath: outputPath,
                        RuntimePath: runtime,
                        DebugName: several ? $"{label} [{e.Entry}]" : label,
                        ClassName: className,
                        MemberName: several ? $"{e.Stage}_{ToIdentifier(e.Entry)}" : e.Stage.ToString()));
                }
            }
        }

        string? catalogFile = manifest.Catalog is null ? null : Path.GetFullPath(manifest.Catalog.File, baseDir);
        return new ShaderPlan(baseDir, manifestFull, includeDirs, entries, catalogFile, manifest.Catalog?.Namespace ?? "GameApp");
    }

    /// <summary>"voxel_chunk" → "VoxelChunk"; недопустимые символы убираются.</summary>
    public static string ToPascal(string text)
    {
        var sb = new StringBuilder();
        bool upper = true;
        foreach (char c in text)
        {
            if (!char.IsLetterOrDigit(c)) { upper = true; continue; }
            sb.Append(upper ? char.ToUpperInvariant(c) : c);
            upper = false;
        }
        if (sb.Length == 0) return "Shader";
        if (char.IsDigit(sb[0])) sb.Insert(0, '_');
        return sb.ToString();
    }

    private static string ToIdentifier(string text)
    {
        var sb = new StringBuilder();
        foreach (char c in text)
            sb.Append(char.IsLetterOrDigit(c) || c == '_' ? c : '_');
        if (sb.Length == 0 || char.IsDigit(sb[0])) sb.Insert(0, '_');
        return sb.ToString();
    }
}