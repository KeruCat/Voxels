using Engine.Memory;
using Friflo.Engine.ECS;

namespace Engine.ECS.Components;
internal struct ChunkData : IComponent {
    internal PoolHandle<ushort> Data;
}
