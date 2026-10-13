using System.Numerics;
using Engine.Core.MathUtil;
using Engine.ECS.Components;
using Engine.Input;
using Friflo.Engine.ECS.Systems;
using Silk.NET.Input;

namespace Engine.ECS.Systems;
internal sealed class PlayerInputSystem : QuerySystem<Camera, Velocity>
{
    private readonly InputState _input;
    private readonly float _speed, _sprint, _smoothing;

    public PlayerInputSystem(InputState input, float speed = 3.5f, float sprintMultiplier = 3f, float smoothing = 14f)
        => (_input, _speed, _sprint, _smoothing) = (input, speed, sprintMultiplier, smoothing);

    protected override void OnUpdate()
    {
        float fwdAxis = Axis(Key.W, Key.S);
        float rgtAxis = Axis(Key.D, Key.A);
        float upAxis  = Axis(Key.Space, Key.ShiftLeft);

        float speed = _speed * (_input.KeyHeld(Key.ControlLeft) ? _sprint : 1f);
        float t = 1f - MathF.Exp(-_smoothing * Tick.deltaTime);   

        foreach (var (cameras, velocities, entities) in Query.Chunks)
        {
            var cams = cameras.Span;
            var vels = velocities.Span;
            for (int i = 0; i < entities.Length; i++)
            {
                ref readonly var cam = ref cams[i];
                ref var vel = ref vels[i];

                Vector3 wish = CameraMath.Forward(in cam) * fwdAxis
                             + CameraMath.Right(in cam)   * rgtAxis
                             + Vector3.UnitY              * upAxis;

                Vector3 target = wish.LengthSquared() > 1e-6f
                    ? Vector3.Normalize(wish) * speed
                    : Vector3.Zero;

                Vector3 v = Vector3.Lerp(new Vector3(vel.X, vel.Y, vel.Z), target, t);
                vel.X = v.X; vel.Y = v.Y; vel.Z = v.Z;
            }
        }
    }

    private float Axis(Key plus, Key minus)
        => (_input.KeyHeld(plus) ? 1f : 0f) - (_input.KeyHeld(minus) ? 1f : 0f);
}