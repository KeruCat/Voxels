using System.Diagnostics;

namespace ShaderTool;

/// <summary>Тонкая обёртка над slangc: одна точка входа → один текст HLSL.</summary>
public sealed class SlangCompiler(string exePath)
{
    public string ExePath { get; } = exePath;

    /// <summary>Ищет slangc: --slangc, переменная SLANGC, Vulkan SDK, затем PATH.</summary>
    public static string Locate(string? explicitPath)
    {
        string exeName = OperatingSystem.IsWindows() ? "slangc.exe" : "slangc";
        string? sdk = Environment.GetEnvironmentVariable("VULKAN_SDK");

        var candidates = new List<string?>
        {
            explicitPath,
            Environment.GetEnvironmentVariable("SLANGC"),
            sdk is null ? null : Path.Combine(sdk, "Bin", exeName),
            sdk is null ? null : Path.Combine(sdk, "bin", exeName),
        };
        foreach (string? candidate in candidates)
            if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
                return candidate;

        string path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (string dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string full = Path.Combine(dir.Trim('"'), exeName);
            if (File.Exists(full))
                return full;
        }

        throw new ShaderToolException(
            "slangc not found. Pass --slangc <path>, set the SLANGC variable, " +
            "install the Vulkan SDK (it ships slangc) or add slangc to PATH.");
    }

    /// <param name="diagnostics">Предупреждения slangc (stderr) при успешной сборке.</param>
    public string CompileToHlsl(string source, string entry, ShaderStage stage,
        IEnumerable<string> includeDirs, out string diagnostics)
    {
        string temp = Path.Combine(Path.GetTempPath(), $"shadertool_{Guid.NewGuid():N}.hlsl");
        try
        {
            var psi = new ProcessStartInfo(ExePath)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add(source);
            psi.ArgumentList.Add("-target"); psi.ArgumentList.Add("hlsl");
            psi.ArgumentList.Add("-entry"); psi.ArgumentList.Add(entry);
            psi.ArgumentList.Add("-stage"); psi.ArgumentList.Add(stage == ShaderStage.Vertex ? "vertex" : "fragment");
            // Матрицы должны лежать row-major: так их пишет C# (System.Numerics) и этого ждёт #pragma pack_matrix
            psi.ArgumentList.Add("-matrix-layout-row-major");
            foreach (string dir in includeDirs)
                psi.ArgumentList.Add("-I" + dir);
            psi.ArgumentList.Add("-o"); psi.ArgumentList.Add(temp);

            using var process = Process.Start(psi)
                ?? throw new ShaderToolException($"could not start {ExePath}");

            // Оба потока читаем параллельно, иначе возможен deadlock на заполненном буфере
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            process.WaitForExit();

            diagnostics = (stderr.Result + stdout.Result).Trim();
            if (process.ExitCode != 0 || !File.Exists(temp))
                throw new ShaderToolException($"slangc failed (exit {process.ExitCode}) on {entry}:\n{diagnostics}");

            return File.ReadAllText(temp);
        }
        finally
        {
            if (File.Exists(temp))
                File.Delete(temp);
        }
    }
}
