using Engine.Core;
using Engine.ECS.Components;
using Engine.ECS.Markers;
using Engine.Graphics;
using Engine.Voxels;
using Friflo.Engine.ECS;
using Friflo.Engine.ECS.Systems;
using System.Numerics;

namespace Engine.ECS.Systems;

/// <summary>
/// Ставит/снимает тег Visible по фрустуму. Один проход, без промежуточных списков:
/// изменения тегов пишутся в CommandBuffer и применяются сразу после прохода,
/// чтобы ChunkRenderSystem в этом же кадре увидел актуальную видимость.
/// </summary>
internal sealed class ChunkCullingSystem : QuerySystem<ChunkCoord, GpuMesh>
{
    private readonly FrameContext _frame;
    private readonly FloatingOrigin _origin;

    public ChunkCullingSystem(FrameContext frame, FloatingOrigin origin) => (_frame, _origin) = (frame, origin);

    protected override void OnUpdate()
    {
        var ecb = CommandBuffer;
        bool changed = false;
        int ox = _origin.X, oy = _origin.Y, oz = _origin.Z;

        foreach (var (coords, gpus, entities) in Query.Chunks)
        {
            var cs = coords.Span;
            var gs = gpus.Span;
            for (int i = 0; i < entities.Length; i++)
            {
                // AABB в локальном (относительно origin) пространстве: разность целых, потом во float — точно
                var min = new Vector3(cs[i].X - ox, cs[i].Y - oy, cs[i].Z - oz) * Chunk.Size;
                var max = min + new Vector3(Chunk.Size);
                bool visible = gs[i].IndexCount > 0 && _frame.IntersectsAabb(min, max);

                if (visible && gs[i].Age < 0f) gs[i].Age = AppClock.Seconds;   // старт анимации появления

                bool was = entities.EntityAt(i).Tags.Has<Visible>();
                if (visible == was) continue;

                if (visible) ecb.AddTag<Visible>(entities[i]);
                else         ecb.RemoveTag<Visible>(entities[i]);
                changed = true;
            }
        }

        // Playback нельзя вызывать внутри цикла по запросу — поэтому здесь, после него
        if (changed) ecb.Playback();
    }
}
