using Engine.App;
using Engine.Core;
using Engine.ECS.Systems;
using Engine.Graphics;
using Engine.Graphics.Memory;
using Engine.Input;
using Engine.Memory;
using Engine.Voxels;
using Engine.Voxels.Generators;
using Friflo.Engine.ECS;
using Friflo.Engine.ECS.Systems;
using Silk.NET.Windowing;

namespace Engine.ECS;
internal static class RegisterECS
{
    public static (EntityStore world, SystemRoot update, SystemRoot render)
    Build(IWindow window, InputState input, MemoryPool pool, GraphicsContext gfx,
          GpuArena vtxArena, GpuArena idxArena, ChunkRenderer chunkRenderer, Viewport viewport,
          FloatingOrigin origin, WorldSettings settings, WorldStats stats, Func<IChunkGenerator> generatorFactory, bool isOpenGL)
    {
        Schema.EnsureRegistered();
        var frame = new FrameContext();
        var world = new EntityStore();
        var map = new ChunkMap();
        var state = new PipelineState();

        var update = new SystemRoot(world) {
            new AppCommandsSystem(window, input),
            new CameraLookSystem(input),
            new PlayerInputSystem(input, speed: 20f, sprintMultiplier: 6f),
            new MovementSystem(),                                   // double-позиция
            new FloatingOriginSystem(origin, stats),                // origin + локальная float-позиция
            new ChunkStreamingSystem(world, map, pool, vtxArena, idxArena, settings, stats, state),
            new ChunkGenerationSystem(pool, generatorFactory, origin, settings, state),
            new ChunkMeshingSystem(pool, map, origin, settings, state),
        };
        var render = new SystemRoot(world)
        {
            new CameraMatrixSystem(viewport, frame, isOpenGL),
            new ChunkUploadSystem(pool, gfx, vtxArena, idxArena, settings, origin),
            new ChunkCullingSystem(frame, origin),
            new ChunkRenderSystem(chunkRenderer, frame, origin, stats),
        };
        return (world, update, render);
    }
}
