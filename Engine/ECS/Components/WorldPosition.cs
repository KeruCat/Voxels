using Friflo.Engine.ECS;

namespace Engine.ECS.Components;

/// <summary>Истинная позиция в мире, double. Position (float) — это её же значение относительно FloatingOrigin.</summary>
internal struct WorldPosition : IComponent
{
    public double X, Y, Z;
}
