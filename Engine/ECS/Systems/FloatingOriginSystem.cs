using Engine.Core;
using Engine.ECS.Components;
using Engine.Voxels;
using Friflo.Engine.ECS.Systems;

namespace Engine.ECS.Systems;

/// <summary>
/// Двигает начало отсчёта вслед за игроком и пересчитывает локальную float-позицию.
/// Рассчитано на одну «камеру»: если появятся другие динамические объекты, им тоже нужен WorldPosition,
/// а локальную позицию они получают тем же вычитанием.
/// </summary>
internal sealed class FloatingOriginSystem(FloatingOrigin origin, WorldStats stats) : QuerySystem<WorldPosition, Position>
{
    protected override void OnUpdate()
    {
        foreach (var (worlds, positions, entities) in Query.Chunks)
        {
            var w = worlds.Span;
            var p = positions.Span;
            for (int i = 0; i < entities.Length; i++)
            {
                origin.Follow(w[i].X, w[i].Y, w[i].Z);
                p[i].X = (float)origin.ToLocalX(w[i].X);
                p[i].Y = (float)origin.ToLocalY(w[i].Y);
                p[i].Z = (float)origin.ToLocalZ(w[i].Z);
                stats.PlayerX = w[i].X; stats.PlayerY = w[i].Y; stats.PlayerZ = w[i].Z;
            }
        }
        stats.OriginX = origin.X; stats.OriginY = origin.Y; stats.OriginZ = origin.Z;
    }
}
