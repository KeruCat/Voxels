using System.Numerics;

namespace Engine.Core.MathUtil;

internal static class CameraMath
{
    public const float MaxPitch = MathF.PI / 2f - 0.01f;

    public static Vector3 Forward(Camera c)
    {
        float cp = MathF.Cos(c.Pitch);
        return new Vector3(cp * MathF.Sin(c.Yaw), MathF.Sin(c.Pitch), cp * MathF.Cos(c.Yaw));
    }

    // Равно Normalize(Cross(forward, UnitY)), но считается только из yaw: нет вырождения при взгляде вверх/вниз.
    public static Vector3 Right(Camera c) => new(-MathF.Cos(c.Yaw), 0f, MathF.Sin(c.Yaw));
}
