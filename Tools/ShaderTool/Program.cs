namespace ShaderTool;

internal static class Program
{
    private const string Usage = """
        ShaderTool: Slang → HLSL для Diligent (D3D11 / D3D12 / Vulkan / OpenGL)

          ShaderTool build [shaders.json] [--slangc <path>] [--force]
              Собирает всё из манифеста; пересобирает только устаревшее.

          ShaderTool fix <in.hlsl> <out.hlsl> --stage vertex|fragment --entry <name>
              Только пост-обработка готового HLSL (без slangc).
        """;

    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0 || args[0] is "-h" or "--help")
            {
                Console.WriteLine(Usage);
                return args.Length == 0 ? 1 : 0;
            }

            var rest = args.Skip(1).ToList();
            return args[0] switch
            {
                "build" => Build(rest),
                "fix" => Fix(rest),
                _ => throw new ShaderToolException($"unknown command '{args[0]}'\n\n{Usage}")
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 1;
        }
    }

    private static int Build(List<string> args)
    {
        string? slangc = TakeOption(args, "--slangc");
        bool force = TakeFlag(args, "--force");
        string manifestPath = args.Count > 0 ? args[0] : "shaders.json";

        var plan = ShaderPlanner.Create(Manifest.Load(manifestPath), manifestPath);
        return new ShaderBuilder(plan, slangc, force).Run();
    }

    private static int Fix(List<string> args)
    {
        string stage = TakeOption(args, "--stage") ?? throw new ShaderToolException("--stage is required");
        string entry = TakeOption(args, "--entry") ?? throw new ShaderToolException("--entry is required");
        if (args.Count != 2)
            throw new ShaderToolException("fix needs exactly two paths: <in.hlsl> <out.hlsl>");

        var parsedStage = ShaderStages.Parse(stage);
        var warnings = new List<string>();
        string result = HlslPostProcessor.Process(File.ReadAllText(args[0]), parsedStage, entry, warnings);

        ShaderBuilder.WriteText(args[1], result);
        Console.WriteLine($"ok    {args[1]}");
        foreach (string warning in warnings)
            Console.WriteLine($"warn  {warning}");
        return 0;
    }

    private static string? TakeOption(List<string> args, string name)
    {
        int i = args.IndexOf(name);
        if (i < 0) return null;
        if (i + 1 >= args.Count) throw new ShaderToolException($"{name} needs a value");
        string value = args[i + 1];
        args.RemoveRange(i, 2);
        return value;
    }

    private static bool TakeFlag(List<string> args, string name) => args.Remove(name);
}
