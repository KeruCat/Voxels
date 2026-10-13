using System.Numerics;

namespace Engine.Graphics;

/// <summary>Проверка видимости AABB (фрустум). Реализует FrameContext.</summary>
public interface IAabbTest
{
    bool Intersects(Vector3 min, Vector3 max);
}
