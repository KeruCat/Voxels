using System.Numerics;
using Engine.Core.MathUtil;
using Engine.ECS.Components;
using Engine.Input;
using Friflo.Engine.ECS.Systems;
using Silk.NET.Input;

namespace Engine.ECS.Systems;
internal sealed class CameraLookSystem : QuerySystem<Camera>
{
    private readonly InputState _input;
    private readonly float _sensitivity;          // радиан на пиксель, ~0.0025

    public CameraLookSystem(InputState input, float sensitivity = 0.0025f) => (_input, _sensitivity) = (input, sensitivity);

    protected override void OnUpdate()
    {
        if (!_input.MouseHeld(MouseButton.Right)) return;

        Vector2 d = _input.MouseDelta;
        if (d == Vector2.Zero) return;

        foreach (var (cameras, entities) in Query.Chunks)
        {
            var cams = cameras.Span;
            for (int i = 0; i < entities.Length; i++)
            {
                ref var cam = ref cams[i];
                cam.Yaw   = MathF.IEEERemainder(cam.Yaw - d.X * _sensitivity, MathF.Tau);
                cam.Pitch = Math.Clamp(cam.Pitch - d.Y * _sensitivity,
                                       -CameraMath.MaxPitch, CameraMath.MaxPitch);
            }
        }
    }
}