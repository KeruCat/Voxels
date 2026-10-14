module voxel_common;

public struct Constants
{
    public float4x4 ViewProjection;
    public float4 Fade;        // x = текущее время (AppClock.Seconds), y = длительность появления
    public float4 AtlasInfo;   // x = колонок, y = строк, z = ширина атласа px, w = высота атласа px
};

static const float FaceShade[6] = { 0.80, 0.80, 1.00, 0.50, 0.90, 0.70 };
static const float AoCurve[4] = { 0.40, 0.60, 0.80, 1.00 };

public uint UnpackFace(uint faceByte) { return faceByte & 7u; }
public uint UnpackAO(uint faceByte)   { return (faceByte >> 3) & 3u; }

// затенение грани × ambient occlusion вершины
public float ShadeFor(uint faceByte)
{
    return FaceShade[UnpackFace(faceByte)] * AoCurve[UnpackAO(faceByte)];
}

// Упорядоченный дизеринг 4×4 (Байер): порог в (0,1) для пикселя
static const float Bayer4[16] =
{
     0.0,  8.0,  2.0, 10.0,
    12.0,  4.0, 14.0,  6.0,
     3.0, 11.0,  1.0,  9.0,
    15.0,  7.0, 13.0,  5.0
};

public float BayerThreshold(float2 pixel)
{
    uint2 p = uint2(pixel) & 3u;
    return (Bayer4[p.y * 4u + p.x] + 0.5) / 16.0;
}
