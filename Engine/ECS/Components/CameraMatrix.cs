using System.Numerics;
using Friflo.Engine.ECS;
using Engine.Memory;

namespace Engine.ECS.Components;
internal struct CameraMatrix : IComponent
{
    public PoolHandle<Matrix4x4> ViewMatrixHandle;
    public PoolHandle<Matrix4x4> ProjectionMatrixHandle;
}
