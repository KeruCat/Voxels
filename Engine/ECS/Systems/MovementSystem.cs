using Engine.ECS.Components;
using Friflo.Engine.ECS.Systems;

namespace Engine.ECS.Systems;

/// <summary>Интегрирует скорость в double-позицию: мир не ограничен точностью float.</summary>
internal sealed class MovementSystem : QuerySystem<WorldPosition, Velocity>
{
    protected override void OnUpdate()
    {
        double dt = Tick.deltaTime;
        foreach (var (positions, velocities, entities) in Query.Chunks)
        {
            var p = positions.Span;
            var v = velocities.Span;
            for (int i = 0; i < entities.Length; i++)
            {
                p[i].X += v[i].X * dt;
                p[i].Y += v[i].Y * dt;
                p[i].Z += v[i].Z * dt;
            }
        }
    }
}
