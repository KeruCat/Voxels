using Friflo.Engine.ECS;

namespace Engine.ECS.Components;
internal struct GpuMesh : IComponent
{
    public int VertexOffset, IndexOffset, VertexCount, IndexCount;
    public float Age;      // AppClock.Seconds момента первого показа (для плавного появления); -1 = ещё не показывался
}
