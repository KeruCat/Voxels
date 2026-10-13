using Engine.ECS.Components;
using Engine.ECS.Markers;
using Friflo.Engine.ECS;

namespace Engine.ECS;
internal static class Schema
{
    private static bool _done;
    private static readonly object _lock = new();

    internal static void EnsureRegistered()
    {
        lock (_lock)
        {
            if (_done) return;
            var aot = new NativeAOT();
            aot.RegisterComponent<Camera>();
            aot.RegisterComponent<ChunkData>();
            aot.RegisterComponent<Mesh>();
            aot.RegisterComponent<Components.Position>();
            aot.RegisterComponent<Velocity>();
            aot.RegisterComponent<CameraMatrix>();
            aot.RegisterComponent<ChunkCoord>();
            aot.RegisterComponent<GpuMesh>();
            aot.RegisterComponent<WorldPosition>();
            aot.RegisterTag<Generated>();
            aot.RegisterTag<Dirty>();
            aot.RegisterTag<NeedsUpload>();
            aot.RegisterTag<Visible>();
            aot.CreateSchema();
            _done = true;
        }
    }
}
