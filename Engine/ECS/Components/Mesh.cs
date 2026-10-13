using Engine.Memory;
using Friflo.Engine.ECS;

namespace Engine.ECS.Components;
internal struct Mesh : IComponent {
    public PoolHandle Vertices;
    public PoolHandle Indices;
    public int VertexCount, IndexCount;
    public int Version;
}
