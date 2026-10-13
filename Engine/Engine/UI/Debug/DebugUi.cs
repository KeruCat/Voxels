using System.Numerics;
using Engine.Graphics.Memory;
using Engine.Memory;
using Hexa.NET.ImGui;

namespace Engine.UI.Debug;

public sealed class DebugUi
{
    private float _smoothedMs = 16f;
    private float _statsTimer;
    private PoolStats _cpu;
    private ArenaStats _vtx, _idx;

    /// <summary>Доп. панель игры (мир, настройки). Вызывается внутри окна Debug.</summary>
    public Action? Extra { get; set; }
    public MemoryPool? Pool { get; set; }
    public GpuArena? VertexArena { get; set; }
    public GpuArena? IndexArena { get; set; }

    public void Draw(float deltaSeconds, string apiName)
    {
        _smoothedMs = _smoothedMs * 0.95f + deltaSeconds * 1000f * 0.05f;

        _statsTimer -= deltaSeconds;
        if (_statsTimer <= 0f)
        {
            _statsTimer = 0.25f;
            if (Pool != null) _cpu = Pool.GetStats();
            if (VertexArena != null) _vtx = VertexArena.GetStats();
            if (IndexArena != null) _idx = IndexArena.GetStats();
        }

        ImGui.SetNextWindowSize(new Vector2(380, 0), ImGuiCond.FirstUseEver);
        if (ImGui.Begin("Debug"))
        {
            DrawFrame(apiName);
            Extra?.Invoke();
            if (Pool != null) DrawMemory();
        }
        ImGui.End();
    }

    private void DrawFrame(string apiName)
    {
        ImGui.Text($"API: {apiName}");
        ImGui.Text(FormattableString.Invariant($"Frame: {_smoothedMs:F2} ms ({1000f / _smoothedMs:F0} FPS)"));

    }
    private void DrawMemory()
    {
        if (!ImGui.CollapsingHeader("Memory", ImGuiTreeNodeFlags.DefaultOpen)) return;

        DrawBlock("CPU pool", _cpu.Used, _cpu.Capacity, _cpu.LargestFreeBlock,
                  _cpu.FreeBlocks, _cpu.Fragmentation, $"allocations: {_cpu.LiveAllocations}");
        if (VertexArena != null) DrawArena("GPU vertices", _vtx);
        if (IndexArena != null) DrawArena("GPU indices", _idx);
    }

    private static void DrawArena(string name, ArenaStats s)
    {
        long k = s.Stride;
        DrawBlock(name, s.Used * k, s.Capacity * k, s.LargestFree * k, s.FreeRanges, s.Fragmentation,
                  FormattableString.Invariant($"{s.Used:N0} / {s.Capacity:N0} items"));
    }

    private static void DrawBlock(string name, long used, long capacity, long largest,
                                  int freeBlocks, double frag, string extra)
    {
        ImGui.PushID(name);
        ImGui.SeparatorText(name);

        float fill = capacity > 0 ? (float)((double)used / capacity) : 0f;
        ImGui.ProgressBar(fill, new Vector2(-1, 0),
            FormattableString.Invariant($"{Bytes(used)} / {Bytes(capacity)} ({fill * 100:F1}%)"));
        ImGui.Text($"Free: {Bytes(capacity - used)}  largest: {Bytes(largest)}");
        ImGui.Text($"Free blocks: {freeBlocks}  {extra}");
        ImGui.ProgressBar((float)frag, new Vector2(-1, 0),
            FormattableString.Invariant($"Fragmentation {frag * 100:F1}%"));

        ImGui.PopID();
    }
    private static void Meter(string label, long used, long capacity)
    {
        float fraction = capacity > 0 ? (float)((double)used / capacity) : 0f;
        ImGui.ProgressBar(fraction, new Vector2(-1, 0), $"{Bytes(used)} / {Bytes(capacity)}");
        if (!label.StartsWith("##"))
        {
            ImGui.SameLine();
            ImGui.Text(label);
        }
    }

    private static string Bytes(long b) => b switch
    {
        >= 1L << 30 => FormattableString.Invariant($"{b / (1024.0 * 1024 * 1024):F2} GB"),
        >= 1L << 20 => FormattableString.Invariant($"{b / (1024.0 * 1024):F2} MB"),
        >= 1L << 10 => FormattableString.Invariant($"{b / 1024.0:F1} KB"),
        _ => $"{b} B"
    };
}
