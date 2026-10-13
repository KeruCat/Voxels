using System.Numerics;
using Engine.App;
using Engine.Core.MathUtil;
using Engine.ECS.Components;
using Engine.Graphics;
using Friflo.Engine.ECS.Systems;

namespace Engine.ECS.Systems;

/// <summary>Строит view/proj из ЛОКАЛЬНОЙ позиции (относительно floating origin) и кладёт в FrameContext вместе с фрустумом.</summary>
internal sealed class CameraMatrixSystem : QuerySystem<Position, Camera>
{
    private readonly Viewport _viewport;
    private readonly FrameContext _frame;
    private readonly bool _isOpenGL;

    public CameraMatrixSystem(Viewport viewport, FrameContext frame, bool isOpenGL)
        => (_viewport, _frame, _isOpenGL) = (viewport, frame, isOpenGL);

    protected override void OnUpdate()
    {
        float aspect = _viewport.Aspect;

        foreach (var (positions, cameras, entities) in Query.Chunks)
        {
            var pos = positions.Span;
            var cams = cameras.Span;

            for (int i = 0; i < entities.Length; i++)
            {
                ref var cam = ref cams[i];
                Vector3 eye = new(pos[i].X, pos[i].Y, pos[i].Z);

                Vector3 fwd   = CameraMath.Forward(in cam);
                Vector3 right = CameraMath.Right(in cam);
                Vector3 up    = Vector3.Cross(right, fwd);
                StreamingFocus.Forward = CameraMath.Forward(in cam);
                StreamingFocus.Position = new(pos[i].X,pos[i].Y,pos[i].Z);
                cam.Target = eye + fwd;

                var view = Matrix4x4.CreateLookAt(eye, cam.Target, up);
                var proj = MathUtil.CreatePerspectiveFieldOfView(cam.FovY, aspect, cam.Near, cam.Far, _isOpenGL);
                _frame.Set(view, proj, eye, _isOpenGL);
            }
        }
    }
}
