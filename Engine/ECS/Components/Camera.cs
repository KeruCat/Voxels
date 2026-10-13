using System.Numerics;
using Friflo.Engine.ECS;

namespace Engine.ECS.Components;
internal struct Camera : IComponent
{
    internal Vector3 Target;
    internal float Yaw, Pitch, Distance;
    internal float FovY, Near, Far;
}
