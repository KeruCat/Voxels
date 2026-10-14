using System.Numerics;
using Silk.NET.Input;

namespace Engine.Input;

internal sealed class InputState
{
    public bool UiWantsMouse;
    public bool UiWantsKeyboard;

    private readonly HashSet<Key> down = [];
    private readonly HashSet<Key> pressed = [];

    // кнопки мыши — множествами, а не битовой маской: значения MouseButton не обязаны быть степенями двойки
    // (Left = 0 в битовой маске терялась бы полностью)
    private readonly HashSet<MouseButton> mouseDown = [];
    private readonly HashSet<MouseButton> mouseClicked = [];

    private Vector2 mousePos, mouseDelta;
    private float wheel;
    private bool hasPos;

    internal void OnKeyDown(Key k)
    {
        if (down.Add(k)) pressed.Add(k);
    }

    internal void OnKeyUp(Key k) => down.Remove(k);

    internal void OnMouseDown(MouseButton b)
    {
        if (mouseDown.Add(b)) mouseClicked.Add(b);
    }

    internal void OnMouseUp(MouseButton b) => mouseDown.Remove(b);

    internal void OnMouseMove(Vector2 p)
    {
        if (hasPos) mouseDelta += p - mousePos;
        mousePos = p;
        hasPos = true;
    }

    internal void OnScroll(float dy) => wheel += dy;

    /// <summary>Сбрасывает последнюю позицию курсора: после смены режима мыши первый сдвиг не даёт рывка камеры.</summary>
    public void ResetMouseTracking() => hasPos = false;

    public bool KeyHeld(Key k) => !UiWantsKeyboard && down.Contains(k);
    public bool KeyPressed(Key k) => !UiWantsKeyboard && pressed.Contains(k);
    public bool MouseHeld(MouseButton b) => !UiWantsMouse && mouseDown.Contains(b);
    public bool MouseClicked(MouseButton b) => !UiWantsMouse && mouseClicked.Contains(b);
    public Vector2 MouseDelta => UiWantsMouse ? default : mouseDelta;
    public Vector2 MousePos => mousePos;
    public float Wheel => UiWantsMouse ? 0 : wheel;

    internal void EndFrame()
    {
        pressed.Clear();
        mouseClicked.Clear();
        mouseDelta = default;
        wheel = 0;
    }
}
