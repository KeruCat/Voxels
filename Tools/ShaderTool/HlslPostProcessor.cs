using System.Text.RegularExpressions;

namespace ShaderTool;

/// <summary>
/// Приводит HLSL от slangc к виду, который принимают ВСЕ бэкенды Diligent, включая OpenGL
/// (его конвертер HLSL → GLSL гораздо привередливее, чем DXC).
///
/// Что и почему чинится (каждый пункт найден на практике):
///  1. Блок `#ifdef SLANG_HLSL_ENABLE_NVAPI / #include "nvHLSLExtns.h"` вырезается: конвертер для GL
///     раскрывает #include ДО проверки условия и падает, не найдя файл.
///  2. Блок `#ifndef __DXC_VERSION_MAJOR / #pragma warning` вырезается (GL-конвертер видит его всегда).
///  3. Директивы `#line` вырезаются.
///  4. `cbuffer constants_0 { Constants_0 constants_0; }`: блок и член с одним именем; GLSL от NVIDIA
///     это отвергает ("name already defined"). Блок переименовывается в `cb_constants_0`.
///  5. Вершинный шейдер, возвращающий структуру через `return`, превращается в `void VSMain(..., out T vsOut)`,
///     а пиксельный получает входной параметр с тем же именем `vsOut`. Конвертер называет межстадийные
///     переменные по имени параметра; при разных именах линковка падает:
///     `"_input_0_Color_0" not declared as an output from the previous stage`.
///  6. `#pragma pack_matrix(row_major)` ОСТАЁТСЯ (и добавляется, если его нет): без него матрицы
///     читаются в другом порядке. Конвертер GL его учитывает.
/// </summary>
public static class HlslPostProcessor
{
    /// <summary>Имя параметра, общее для выхода вершинного и входа пиксельного шейдера.</summary>
    public const string InterstageName = "vsOut";

    public const string PackMatrixPragma = "#pragma pack_matrix(row_major)";

    private static readonly string[] BlocksToStrip =
    [
        "#ifdef SLANG_HLSL_ENABLE_NVAPI",
        "#ifndef __DXC_VERSION_MAJOR"
    ];

    public static string Process(string hlsl, ShaderStage stage, string entry, List<string> warnings)
    {
        string s = hlsl.Replace("\r\n", "\n");

        s = StripConditionalBlocks(s, BlocksToStrip);
        s = StripLineDirectives(s);
        s = RenameCBufferBlocks(s);

        s = stage switch
        {
            ShaderStage.Vertex => ReturnToOutParameter(s, entry, warnings),
            ShaderStage.Fragment => RenameInterstageInput(s, entry, warnings),
            _ => s
        };

        if (!s.Contains(PackMatrixPragma))
            s = PackMatrixPragma + "\n" + s;

        s = Tidy(s);
        Validate(s, stage, entry, warnings);
        return s;
    }

    // ───────────── 1–3. препроцессор ─────────────

    /// <summary>Вырезает блоки #if…#endif, начинающиеся с одной из указанных строк (с учётом вложенности).</summary>
    private static string StripConditionalBlocks(string source, string[] startLines)
    {
        var output = new List<string>();
        int depth = 0;

        foreach (string line in source.Split('\n'))
        {
            string t = line.Trim();
            if (depth > 0)
            {
                if (t.StartsWith("#if", StringComparison.Ordinal)) depth++;   // #if, #ifdef, #ifndef
                else if (t.StartsWith("#endif", StringComparison.Ordinal)) depth--;
                continue;
            }
            if (Array.IndexOf(startLines, t) >= 0)
            {
                depth = 1;
                continue;
            }
            output.Add(line);
        }
        return string.Join('\n', output);
    }

    private static string StripLineDirectives(string s)
        => Regex.Replace(s, @"^[ \t]*#line\b.*\n?", "", RegexOptions.Multiline);

    // ───────────── 4. cbuffer ─────────────

    private static readonly Regex SingleMemberCBuffer =
        new(@"cbuffer\s+(?<block>\w+)(?<mid>[^{]*)\{\s*(?<type>\w+)\s+(?<member>\w+)\s*;\s*\}");

    private static string RenameCBufferBlocks(string s)
        => SingleMemberCBuffer.Replace(s, m =>
        {
            string block = m.Groups["block"].Value;
            if (block != m.Groups["member"].Value)
                return m.Value;

            int rel = m.Groups["block"].Index - m.Index;
            return m.Value[..rel] + "cb_" + block + m.Value[(rel + block.Length)..];
        });

    // ───────────── 5. межстадийные данные ─────────────

    /// <summary>`T VSMain(In input) { T output; ...; return output; }` → `void VSMain(In input, out T vsOut) { ...vsOut... }`</summary>
    private static string ReturnToOutParameter(string s, string entry, List<string> warnings)
    {
        var sig = FindEntry(s, entry);
        if (sig is null)
        {
            warnings.Add($"entry point '{entry}' not found in the generated HLSL");
            return s;
        }

        string ret = sig.Groups["ret"].Value;
        if (ret == "void")
            return s;   // уже в нужной форме
        if (sig.Groups["semantic"].Success)
        {
            warnings.Add($"'{entry}' returns '{ret}' with a semantic; cannot convert to an out parameter");
            return s;
        }

        int open = sig.Index + sig.Length - 1;
        int close = FindClosingBrace(s, open);
        if (close < 0)
        {
            warnings.Add($"unbalanced braces in '{entry}'");
            return s;
        }

        string body = s.Substring(open + 1, close - open - 1);
        var decl = Regex.Match(body, $@"^[ \t]*{Regex.Escape(ret)}[ \t]+(?<v>\w+)[ \t]*;[ \t]*\n", RegexOptions.Multiline);
        if (!decl.Success)
        {
            warnings.Add($"'{entry}': local variable of type '{ret}' not found; left as is");
            return s;
        }

        string local = decl.Groups["v"].Value;
        body = body.Remove(decl.Index, decl.Length);
        body = Regex.Replace(body, $@"^[ \t]*return[ \t]+{Regex.Escape(local)}[ \t]*;[ \t]*\n?", "", RegexOptions.Multiline);
        body = Regex.Replace(body, $@"\b{Regex.Escape(local)}\b", InterstageName);

        if (Regex.IsMatch(body, @"\breturn\b"))
            warnings.Add($"'{entry}' still has a return statement after conversion (several returns are not supported)");

        string parameters = sig.Groups["params"].Value.Trim();
        string separator = parameters.Length == 0 ? "" : ", ";
        string header = $"void {entry}({parameters}{separator}out {ret} {InterstageName})\n{{";

        return s[..sig.Index] + header + body + s[close..];
    }

    /// <summary>`float4 FSMain(In input_0) : SV_TARGET` → параметр переименовывается в vsOut.</summary>
    private static string RenameInterstageInput(string s, string entry, List<string> warnings)
    {
        var sig = FindEntry(s, entry);
        if (sig is null)
        {
            warnings.Add($"entry point '{entry}' not found in the generated HLSL");
            return s;
        }

        var param = Regex.Match(sig.Groups["params"].Value, @"^\s*(?<type>\w+)\s+(?<name>\w+)\s*$");
        if (!param.Success)
        {
            warnings.Add($"'{entry}': expected a single 'Type name' parameter, found '{sig.Groups["params"].Value.Trim()}'");
            return s;
        }

        string name = param.Groups["name"].Value;
        if (name == InterstageName)
            return s;

        int open = sig.Index + sig.Length - 1;
        int close = FindClosingBrace(s, open);
        if (close < 0)
        {
            warnings.Add($"unbalanced braces in '{entry}'");
            return s;
        }

        // сигнатура: подменяем только текст параметров
        var paramsGroup = sig.Groups["params"];
        int paramsRel = paramsGroup.Index - sig.Index;
        string newSig = sig.Value[..paramsRel]
                        + param.Groups["type"].Value + " " + InterstageName
                        + sig.Value[(paramsRel + paramsGroup.Length)..];

        string body = s.Substring(open + 1, close - open - 1);
        body = Regex.Replace(body, $@"\b{Regex.Escape(name)}\b", InterstageName);

        return s[..sig.Index] + newSig + body + s[close..];
    }

    private static Match? FindEntry(string s, string entry)
    {
        var m = Regex.Match(s,
            $@"(?<ret>\w+)\s+{Regex.Escape(entry)}\s*\((?<params>[^)]*)\)\s*(?<semantic>:\s*\w+)?\s*\{{");
        return m.Success ? m : null;
    }

    private static int FindClosingBrace(string s, int open)
    {
        int depth = 0;
        for (int i = open; i < s.Length; i++)
        {
            if (s[i] == '{') depth++;
            else if (s[i] == '}' && --depth == 0) return i;
        }
        return -1;
    }

    // ───────────── финал ─────────────

    private static string Tidy(string s)
    {
        s = Regex.Replace(s, @"\n{3,}", "\n\n");
        s = s.Trim('\n');
        return s + "\n";
    }

    /// <summary>Самопроверка результата: ловит то, что позже превратится в загадочную ошибку на OpenGL.</summary>
    private static void Validate(string s, ShaderStage stage, string entry, List<string> warnings)
    {
        if (Regex.IsMatch(s, @"^\s*#\s*include", RegexOptions.Multiline))
            warnings.Add("#include remained in the output: the OpenGL converter cannot open it");

        foreach (Match m in Regex.Matches(s, @"cbuffer\s+(?<block>\w+)[^{]*\{(?<body>[^}]*)\}"))
        {
            string block = m.Groups["block"].Value;
            if (Regex.IsMatch(m.Groups["body"].Value, $@"\b{Regex.Escape(block)}\b"))
                warnings.Add($"cbuffer '{block}' has a member with the same name (OpenGL: \"name is already defined\")");
        }

        string e = Regex.Escape(entry);
        string pattern = stage == ShaderStage.Vertex
            ? $@"void\s+{e}\s*\([^)]*\bout\s+\w+\s+{InterstageName}\b"
            : $@"\b{e}\s*\([^)]*\b{InterstageName}\b";
        if (!Regex.IsMatch(s, pattern))
            warnings.Add($"'{entry}' does not use the shared '{InterstageName}' interstage parameter: " +
                         "OpenGL may fail to link (\"not declared as an output from the previous stage\")");
    }
}
