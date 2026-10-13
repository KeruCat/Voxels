#pragma pack_matrix(row_major)

struct Constants_0
{
    float4x4 ViewProjection_0;
    float4 Fade_0;
};

cbuffer cb_constants_0 : register(b0)
{
    Constants_0 constants_0;
}

static const float  AoCurve_0[int(4)] = { 0.40000000596046448f, 0.60000002384185791f, 0.80000001192092896f, 1.0f };

static const float  FaceShade_0[int(6)] = { 0.80000001192092896f, 0.80000001192092896f, 1.0f, 0.5f, 0.89999997615814209f, 0.69999998807907104f };

static const float3  Palette_0[int(8)] = { float3(1.0f, 0.0f, 1.0f), float3(0.36000001430511475f, 0.62000000476837158f, 0.2199999988079071f), float3(0.44999998807907104f, 0.31000000238418579f, 0.18999999761581421f), float3(0.5f, 0.5f, 0.51999998092651367f), float3(0.85000002384185791f, 0.77999997138977051f, 0.51999998092651367f), float3(0.18000000715255737f, 0.37999999523162842f, 0.77999997138977051f), float3(0.94999998807907104f, 0.95999997854232788f, 0.98000001907348633f), float3(0.60000002384185791f, 0.60000002384185791f, 0.60000002384185791f) };

uint UnpackBlockId_0(uint4 uvBlock_0)
{
    return (uvBlock_0.z) | ((uvBlock_0.w) << int(8));
}

float3 BlockColor_0(uint id_0)
{
    return Palette_0[min(id_0, 7U)];
}

uint UnpackFace_0(uint faceByte_0)
{

    return faceByte_0 & 7U;
}

uint UnpackAO_0(uint faceByte_1)
{

    return (faceByte_1 >> int(3)) & 3U;
}

float3 ShadedBlockColor_0(uint blockId_0, uint faceByte_2)
{
    return BlockColor_0(blockId_0) * FaceShade_0[UnpackFace_0(faceByte_2)] * AoCurve_0[UnpackAO_0(faceByte_2)];
}

struct FSInput_0
{
    float4 Position_0 : SV_POSITION;
    float4 Color_0 : COLOR00;
    float Opacity_0 : TEXCOORD00;
};

struct VSInput_0
{
    uint4 PosFace_0 : ATTRIB0;
    uint4 UvBlock_0 : ATTRIB1;
    float4 ChunkOrigin_0 : ATTRIB2;
};

void VSMain(VSInput_0 input_0, out FSInput_0 vsOut)
{

    float t_0 = saturate((constants_0.Fade_0.x - input_0.ChunkOrigin_0.w) / max(constants_0.Fade_0.y, 0.00009999999747379f));

    vsOut.Position_0 = mul(float4(float3(input_0.PosFace_0.xyz) + input_0.ChunkOrigin_0.xyz, 1.0f), constants_0.ViewProjection_0);
    vsOut.Color_0 = float4(ShadedBlockColor_0(UnpackBlockId_0(input_0.UvBlock_0), input_0.PosFace_0.w), 1.0f);
    vsOut.Opacity_0 = t_0 * t_0 * (3.0f - 2.0f * t_0);
}
