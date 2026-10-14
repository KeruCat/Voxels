import voxel_common;

struct VSInput
{
    uint4 PosFace : ATTRIB0;       // x, y, z (0..16), w = грань (биты 0..2) + AO (биты 3..4)
    uint Packed : ATTRIB1;         // тайл (0..7) | повтор U (8..12) | повтор V (13..17) | угол U (18) | угол V (19)
    float4 ChunkOrigin : ATTRIB2;  // per-instance: xyz = origin чанка (floating origin), w = время начала показа
};

struct FSInput
{
    float4 Position : SV_POSITION;
    float Shade : COLOR0;          // затенение грани × AO, одинаково для всех точек вершины
    float2 TileOrigin : TEXCOORD0; // UV левого верхнего угла тайла в атласе
    float2 TileSize : TEXCOORD1;   // размер тайла в UV
    float2 Repeat : TEXCOORD2;     // координата внутри слитого квада в тайлах (0..повтор)
    float Opacity : TEXCOORD3;     // 0..1, одинакова для всего чанка
};

ConstantBuffer<Constants> constants;
Texture2D atlas;
SamplerState atlas_sampler;

[shader("vertex")]
FSInput VSMain(VSInput input)
{
    uint tile = input.Packed & 0xFFu;
    uint repU = (input.Packed >> 8) & 31u;
    uint repV = (input.Packed >> 13) & 31u;
    uint cornerU = (input.Packed >> 18) & 1u;
    uint cornerV = (input.Packed >> 19) & 1u;

    float cols = constants.AtlasInfo.x;
    float rows = constants.AtlasInfo.y;
    float2 cell = float2(float(tile % uint(cols)), float(tile / uint(cols)));

    float4 world = float4(float3(input.PosFace.xyz) + input.ChunkOrigin.xyz, 1.0);
    float t = saturate((constants.Fade.x - input.ChunkOrigin.w) / max(constants.Fade.y, 1e-4));

    FSInput output;
    output.Position = mul(world, constants.ViewProjection);
    output.Shade = ShadeFor(input.PosFace.w);
    output.TileOrigin = cell / float2(cols, rows);
    output.TileSize = float2(1.0 / cols, 1.0 / rows);
    output.Repeat = float2(cornerU != 0 ? float(repU) : 0.0, cornerV != 0 ? float(repV) : 0.0);
    output.Opacity = t * t * (3.0 - 2.0 * t);
    return output;
}

[shader("fragment")]
float4 FSMain(FSInput input) : SV_TARGET
{
    if (input.Opacity < 1.0 && input.Opacity <= BayerThreshold(input.Position.xy))
        discard;

    // повтор внутри тайла; верхняя граница чуть меньше 1, чтобы не зайти в соседний тайл
    float2 local = min(frac(input.Repeat), float2(0.9999, 0.9999));
    float2 uv = input.TileOrigin + local * input.TileSize;
    float4 texel = atlas.SampleLevel(atlas_sampler, uv, 0);

    // листва и прочее с прозрачностью
    if (texel.a < 0.5)
        discard;

    return float4(texel.rgb * input.Shade, 1.0);
}
