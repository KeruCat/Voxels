using Engine.Graphics;
using Hexa.NET.ImGui;
using Silk.NET.Input;
using Silk.NET.Windowing;

namespace Engine.UI.ImGUI;

/// <summary>
/// Весь жизненный цикл ImGui в одном месте: контекст, ввод, рендерер.
/// Кадр: <see cref="BeginFrame"/> (в Update) → рисуем окна → <see cref="Render"/> (в Render, после сцены).
/// </summary>
internal sealed class ImGuiLayer : IDisposable
{
    private readonly ImGuiContextPtr _context;
    private readonly ImGuiInput _input;
    private readonly ImGuiRenderer _renderer;
    private bool _frameBegun;
    private bool _disposed;

    public ImGuiLayer(IWindow window, IInputContext inputContext, GraphicsContext gfx)
    {
        // Контекст должен существовать до ImGuiInput и ImGuiRenderer: оба сразу обращаются к ImGui.GetIO().
        _context = ImGui.CreateContext();
        ImGui.SetCurrentContext(_context);

        _input = new ImGuiInput(window, inputContext);
        _renderer = new ImGuiRenderer(gfx);
    }

    public bool WantsMouse => ImGuiInput.WantsMouse;
    public bool WantsKeyboard => ImGuiInput.WantsKeyboard;

    public void BeginFrame(float deltaSeconds)
    {
        _input.Update(deltaSeconds);
        ImGui.NewFrame();
        _frameBegun = true;
    }

    /// <summary>Рисует собранный кадр поверх текущего бэкбуфера. Без BeginFrame ничего не делает.</summary>
    public void Render()
    {
        if (!_frameBegun) return;
        _frameBegun = false;

        ImGui.Render();
        _renderer.Render(ImGui.GetDrawData());
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _renderer.Dispose();
        ImGui.DestroyContext(_context);
    }
}
