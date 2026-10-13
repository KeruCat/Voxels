using System.Numerics;
using Engine.Core;
using Engine.Core.MathUtil;
using Engine.Input;
using Silk.NET.Input;

namespace Game.Gameplay;

/// <summary>
/// Игрок: мировая позиция (double), скорость и камера. Управление — прямо здесь, без систем.
/// Поведение то же, что было в ECS-системах: взгляд ПКМ, движение WASD + Space/Shift, спринт Ctrl.
/// </summary>
internal sealed class Player
{
    private const float LookSensitivity = 0.0025f;   // радиан на пиксель
    private const float Speed = 20f;
    private const float SprintMultiplier = 6f;
    private const float Smoothing = 14f;

    public double X, Y, Z;
    public Vector3 Velocity;
    public Camera Camera { get; } = new() { Pitch = -0.3f };

    internal void Update(float dt, InputState input)
    {
        Look(input);
        Move(dt, input);
    }

    /// <summary>Позиция игрока относительно floating origin: float, маленькая и точная.</summary>
    public Vector3 LocalPosition(FloatingOrigin origin) =>
        new((float)origin.ToLocalX(X), (float)origin.ToLocalY(Y), (float)origin.ToLocalZ(Z));

    private void Look(InputState input)
    {
        if (!input.MouseHeld(MouseButton.Right)) return;

        Vector2 d = input.MouseDelta;
        if (d == Vector2.Zero) return;

        Camera.Yaw = MathF.IEEERemainder(Camera.Yaw - d.X * LookSensitivity, MathF.Tau);
        Camera.Pitch = Math.Clamp(Camera.Pitch - d.Y * LookSensitivity, -CameraMath.MaxPitch, CameraMath.MaxPitch);
    }

    private void Move(float dt, InputState input)
    {
        float fwd = Axis(input, Key.W, Key.S);
        float rgt = Axis(input, Key.D, Key.A);
        float up = Axis(input, Key.Space, Key.ShiftLeft);
        float speed = Speed * (input.KeyHeld(Key.ControlLeft) ? SprintMultiplier : 1f);

        Vector3 wish = CameraMath.Forward(Camera) * fwd
                     + CameraMath.Right(Camera) * rgt
                     + Vector3.UnitY * up;
        Vector3 target = wish.LengthSquared() > 1e-6f ? Vector3.Normalize(wish) * speed : Vector3.Zero;

        Velocity = Vector3.Lerp(Velocity, target, 1f - MathF.Exp(-Smoothing * dt));

        // интеграция в double: мир не ограничен точностью float
        X += Velocity.X * dt;
        Y += Velocity.Y * dt;
        Z += Velocity.Z * dt;
    }

    private static float Axis(InputState input, Key plus, Key minus) =>
        (input.KeyHeld(plus) ? 1f : 0f) - (input.KeyHeld(minus) ? 1f : 0f);
}
