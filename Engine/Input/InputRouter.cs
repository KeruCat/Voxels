using Silk.NET.Input;

namespace Engine.Input;

/// <summary>Подписывает <see cref="InputState"/> на события мыши и клавиатуры Silk.NET.</summary>
internal static class InputRouter
{
    public static void Attach(IInputContext context, InputState state)
    {
        foreach (var mouse in context.Mice)
        {
            mouse.MouseDown += (_, button) => state.OnMouseDown(button);
            mouse.MouseUp   += (_, button) => state.OnMouseUp(button);
            mouse.MouseMove += (_, pos) => state.OnMouseMove(new(pos.X, pos.Y));
            mouse.Scroll    += (_, wheel) => state.OnScroll(wheel.Y);
        }

        foreach (var keyboard in context.Keyboards)
        {
            keyboard.KeyDown += (_, key, _) => state.OnKeyDown(key);
            keyboard.KeyUp   += (_, key, _) => state.OnKeyUp(key);
        }
    }
}
