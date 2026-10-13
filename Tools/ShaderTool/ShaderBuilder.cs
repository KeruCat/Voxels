using System.Text;

namespace ShaderTool;

/// <summary>Идёт по плану: каталог → для каждой точки входа slangc → пост-обработка → запись. Пересобирает только устаревшее.</summary>
public sealed class ShaderBuilder(ShaderPlan plan, string? slangcPath, bool force)
{
    private SlangCompiler? _slangc;

    public int Run()
    {
        // Каталог зависит только от манифеста и не требует slangc, поэтому обновляется всегда
        if (plan.CatalogFile is not null)
        {
            bool changed = CatalogWriter.WriteIfChanged(plan.CatalogFile, CatalogWriter.Generate(plan));
            Console.WriteLine($"{(changed ? "ok   " : "skip ")} {Path.GetFileName(plan.CatalogFile)} ({(changed ? "updated" : "unchanged")})");
        }

        // Всё, от чего зависит результат: все .slang из include-папок (включая вложенные), манифест и сама тулза
        var inputs = plan.IncludeDirs
            .SelectMany(dir => Directory.EnumerateFiles(dir, "*.slang", SearchOption.AllDirectories))
            .Append(plan.ManifestPath)
            .Append(typeof(ShaderBuilder).Assembly.Location)
            .Where(File.Exists)
            .Distinct()
            .ToList();

        int failed = 0;
        foreach (var shader in plan.Entries)
        {
            string label = shader.RuntimePath;
            try
            {
                if (!force && IsUpToDate(shader.OutputPath, inputs))
                {
                    Console.WriteLine($"skip  {label} (up to date)");
                    continue;
                }

                _slangc ??= new SlangCompiler(SlangCompiler.Locate(slangcPath));

                string raw = _slangc.CompileToHlsl(shader.SourcePath, shader.Entry, shader.Stage, plan.IncludeDirs, out string diagnostics);
                if (diagnostics.Length > 0)
                    Console.WriteLine($"slangc: {diagnostics}");

                var warnings = new List<string>();
                string hlsl = HlslPostProcessor.Process(raw, shader.Stage, shader.Entry, warnings);

                WriteText(shader.OutputPath, hlsl);
                Console.WriteLine($"ok    {label} ({shader.SourceRelative}: {shader.Entry}, {shader.Stage.ToString().ToLowerInvariant()})");
                foreach (string warning in warnings)
                    Console.WriteLine($"warn  {label}: {warning}");
            }
            catch (Exception ex)
            {
                failed++;
                Console.Error.WriteLine($"FAIL  {label}: {ex.Message}");
            }
        }
        return failed == 0 ? 0 : 1;
    }

    public static void WriteText(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, text, new UTF8Encoding(false));   // без BOM
    }

    private static bool IsUpToDate(string output, List<string> inputs)
    {
        if (!File.Exists(output))
            return false;
        DateTime outputTime = File.GetLastWriteTimeUtc(output);
        return inputs.All(input => File.GetLastWriteTimeUtc(input) <= outputTime);
    }
}