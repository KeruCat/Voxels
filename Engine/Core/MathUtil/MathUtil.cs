using System.Numerics;

namespace Engine.Core.MathUtil;

internal static class MathUtil
{
    public static Matrix4x4 CreatePerspectiveFieldOfView(float fieldOfView, float aspectRatio, float nearPlaneDistance, float farPlaneDistance, bool isOpenGL)
    {
        if (fieldOfView <= 0.0f || fieldOfView >= MathF.PI)
            throw new ArgumentOutOfRangeException(nameof(fieldOfView));

        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(nearPlaneDistance, 0.0f);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(farPlaneDistance, 0.0f);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(nearPlaneDistance, farPlaneDistance);

        float yScale = 1.0f / MathF.Tan(fieldOfView * 0.5f);
        float xScale = yScale / aspectRatio;

        Matrix4x4 result = new()
        {
            M11 = xScale,
            M22 = yScale
        };

        // Правосторонняя система (как Matrix4x4.CreateLookAt): перед камерой z_view < 0.
        if (isOpenGL)
        {
            result.M33 = (farPlaneDistance + nearPlaneDistance) / (nearPlaneDistance - farPlaneDistance);
            result.M43 = 2 * nearPlaneDistance * farPlaneDistance / (nearPlaneDistance - farPlaneDistance);
        }
        else
        {
            result.M33 = farPlaneDistance / (nearPlaneDistance - farPlaneDistance);
            result.M43 = nearPlaneDistance * farPlaneDistance / (nearPlaneDistance - farPlaneDistance);
        }
        result.M34 = -1.0f;

        return result;
    }
}
