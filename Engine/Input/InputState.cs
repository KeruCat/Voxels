using System.Numerics;
using Silk.NET.Input;

namespace Engine.Input;
internal sealed class InputState
{
    public bool UiWantsMouse;
    public bool UiWantsKeyboard;

    private readonly HashSet<Key> down    = [];
    private readonly HashSet<Key> pressed = [];
    private MouseButton buttons;           
    private Vector2 mousePos, mouseDelta;
    private float wheel;
    private bool hasPos;

    internal void OnKeyDown(Key k) { 
        if (down.Add(k)) pressed.Add(k); 
    }
    internal void OnKeyUp(Key k)   => down.Remove(k);
    internal void OnMouseDown(MouseButton b) => buttons |= b;
    internal void OnMouseUp(MouseButton b)   => buttons &= ~b;
    internal void OnMouseMove(Vector2 p) {
        if (hasPos) mouseDelta += p - mousePos;
        mousePos = p; hasPos = true;
    }
    internal void OnScroll(float dy) => wheel += dy;

    public bool KeyHeld(Key k)    => !UiWantsKeyboard && down.Contains(k);
    public bool KeyPressed(Key k) => !UiWantsKeyboard && pressed.Contains(k);
    public bool MouseHeld(MouseButton b) => !UiWantsMouse && (buttons & b) != 0;
    public Vector2 MouseDelta => UiWantsMouse ? default : mouseDelta;
    public Vector2 MousePos   => mousePos;                 
    public float   Wheel      => UiWantsMouse ? 0 : wheel;

    internal void EndFrame() { 
        pressed.Clear();
        mouseDelta = default;
        wheel = 0; 
    }
}
