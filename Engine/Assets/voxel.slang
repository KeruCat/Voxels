import voxel_common;

struct VSInput
{
    uint4 PosFace : ATTRIB0;       // x, y, z (0..16), w = грань (биты 0..2) + AO (биты 3..4)
    uint4 UvBlock : ATTRIB1;       // u, v, blockId (младший байт, старший байт)
    float4 ChunkOrigin : ATTRIB2;  // per-instance: xyz = origin чанка (floating origin), w = время начала показа
};

struct FSInput
{
    float4 Position : SV_POSITION;
    float4 Color : COLOR0;
    float Opacity : TEXCOORD0;     // 0..1, одинакова для всего чанка
};

ConstantBuffer<Constants> constants;

[shader("vertex")]
FSInput VSMain(VSInput input)
{
    float4 world = float4(float3(input.PosFace.xyz) + input.ChunkOrigin.xyz, 1.0);
    float t = saturate((constants.Fade.x - input.ChunkOrigin.w) / max(constants.Fade.y, 1e-4));

    FSInput output;
    output.Position = mul(world, constants.ViewProjection);
    output.Color = float4(ShadedBlockColor(UnpackBlockId(input.UvBlock), input.PosFace.w), 1.0);
    output.Opacity = t * t * (3.0 - 2.0 * t);
    return output;
}

[shader("fragment")]
float4 FSMain(FSInput input) : SV_TARGET
{
    if (input.Opacity < 1.0 && input.Opacity <= BayerThreshold(input.Position.xy))
        discard;

    return input.Color;
}
