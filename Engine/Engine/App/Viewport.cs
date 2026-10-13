using System.Numerics;

namespace Engine.App;
internal sealed class Viewport
{
    public int Width { get; private set; } = 1;
    public int Height { get; private set; } = 1;
    public float Aspect => (float)Width / Height;

    public void Resize(int w, int h)
    {
        if (w > 0 && h > 0) { Width = w; Height = h; }   // свёрнутое окно игнорируем
    }
}