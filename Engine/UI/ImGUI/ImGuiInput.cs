using System.Numerics;
using Hexa.NET.ImGui;
using Silk.NET.Input;
using Silk.NET.Windowing;

namespace Engine.UI.ImGUI;
public sealed class ImGuiInput
{
    private readonly IWindow window;

    public ImGuiInput(IWindow window, IInputContext input)
    {
        this.window = window;

        foreach (var mouse in input.Mice)
        {
            mouse.MouseMove += (_, p) => ImGui.GetIO().AddMousePosEvent(p.X, p.Y);
            mouse.MouseDown += (_, b) => OnMouseButton(b, true);
            mouse.MouseUp += (_, b) => OnMouseButton(b, false);
            mouse.Scroll += (_, s) => ImGui.GetIO().AddMouseWheelEvent(s.X, s.Y);
        }

        foreach (var kb in input.Keyboards)
        {
            kb.KeyDown += (_, key, _) => OnKey(key, true);
            kb.KeyUp += (_, key, _) => OnKey(key, false);
            kb.KeyChar += (_, c) => ImGui.GetIO().AddInputCharacter(c);
        }
    }
    public void Update(float deltaSeconds)
    {
        var io = ImGui.GetIO();
        var size = window.Size;
        var fb = window.FramebufferSize;
        io.BackendFlags |= ImGuiBackendFlags.RendererHasVtxOffset;
        io.BackendFlags |= ImGuiBackendFlags.RendererHasTextures;
        io.DisplaySize = new Vector2(size.X, size.Y);
        io.DisplayFramebufferScale = size.X > 0 && size.Y > 0
            ? new Vector2((float)fb.X / size.X, (float)fb.Y / size.Y)
            : Vector2.One;
        io.DeltaTime = Math.Max(deltaSeconds, 1e-5f);
    }
    public static bool WantsMouse => ImGui.GetIO().WantCaptureMouse;
    public static bool WantsKeyboard => ImGui.GetIO().WantCaptureKeyboard;

    private static void OnMouseButton(MouseButton button, bool down)
    {
        int index = button switch
        {
            MouseButton.Left => 0,
            MouseButton.Right => 1,
            MouseButton.Middle => 2,
            _ => -1
        };
        if (index >= 0)
            ImGui.GetIO().AddMouseButtonEvent(index, down);
    }

    private static void OnKey(Key key, bool down)
    {
        var io = ImGui.GetIO();
        if (TryMap(key, out var imKey))
            io.AddKeyEvent(imKey, down);
        switch (key)
        {
            case Key.ControlLeft or Key.ControlRight: io.AddKeyEvent(ImGuiKey.ModCtrl, down); break;
            case Key.ShiftLeft or Key.ShiftRight: io.AddKeyEvent(ImGuiKey.ModShift, down); break;
            case Key.AltLeft or Key.AltRight: io.AddKeyEvent(ImGuiKey.ModAlt, down); break;
        }
    }

    private static bool TryMap(Key key, out ImGuiKey result)
    {
        if (key >= Key.A && key <= Key.Z) { result = ImGuiKey.A + (key - Key.A); return true; }
        if (key >= Key.Number0 && key <= Key.Number9) { result = ImGuiKey.Key0 + (key - Key.Number0); return true; }

        result = key switch
        {
            Key.Space => ImGuiKey.Space,
            Key.Enter => ImGuiKey.Enter,
            Key.Escape => ImGuiKey.Escape,
            Key.Tab => ImGuiKey.Tab,
            Key.Backspace => ImGuiKey.Backspace,
            Key.Delete => ImGuiKey.Delete,
            Key.Left => ImGuiKey.LeftArrow,
            Key.Right => ImGuiKey.RightArrow,
            Key.Up => ImGuiKey.UpArrow,
            Key.Down => ImGuiKey.DownArrow,
            Key.Home => ImGuiKey.Home,
            Key.End => ImGuiKey.End,
            Key.ControlLeft => ImGuiKey.LeftCtrl,
            Key.ControlRight => ImGuiKey.RightCtrl,
            Key.ShiftLeft => ImGuiKey.LeftShift,
            Key.ShiftRight => ImGuiKey.RightShift,
            Key.AltLeft => ImGuiKey.LeftAlt,
            Key.AltRight => ImGuiKey.RightAlt,
            _ => ImGuiKey.None
        };
        return result != ImGuiKey.None;
    }
}
