using System.Numerics;

namespace Engine.Graphics;

internal sealed class FrameContext
{
    public Matrix4x4 View, Projection, ViewProjection;
    public Vector3 CameraPosition;
    public readonly Vector4[] Planes = new Vector4[6];   // (nx, ny, nz, d), нормали внутрь

    public void Set(in Matrix4x4 view, in Matrix4x4 proj, Vector3 eye, bool glDepth)
    {
        View = view;
        Projection = proj;
        CameraPosition = eye;
        var m = ViewProjection = view * proj;

        // System.Numerics использует v * M, поэтому плоскости берутся из столбцов
        Vector4 c1 = new(m.M11, m.M21, m.M31, m.M41);
        Vector4 c2 = new(m.M12, m.M22, m.M32, m.M42);
        Vector4 c3 = new(m.M13, m.M23, m.M33, m.M43);
        Vector4 c4 = new(m.M14, m.M24, m.M34, m.M44);

        Planes[0] = c4 + c1;                    // left
        Planes[1] = c4 - c1;                    // right
        Planes[2] = c4 + c2;                    // bottom
        Planes[3] = c4 - c2;                    // top
        Planes[4] = glDepth ? c4 + c3 : c3;     // near: z∈[-w,w] (GL) или [0,w] (D3D/VK)
        Planes[5] = c4 - c3;                    // far
    }

    public bool IntersectsAabb(Vector3 min, Vector3 max)
    {
        foreach (var p in Planes)
        {
            // p-вершина: угол бокса, самый дальний по нормали плоскости
            float x = p.X >= 0 ? max.X : min.X;
            float y = p.Y >= 0 ? max.Y : min.Y;
            float z = p.Z >= 0 ? max.Z : min.Z;
            if (p.X * x + p.Y * y + p.Z * z + p.W < 0) return false;
        }
        return true;
    }
}