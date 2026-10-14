#pragma pack_matrix(row_major)

struct Constants_0
{
    float4x4 ViewProjection_0;
    float4 Fade_0;
    float4 AtlasInfo_0;
};

cbuffer cb_constants_0 : register(b0)
{
    Constants_0 constants_0;
}

static const float  AoCurve_0[int(4)] = { 0.40000000596046448f, 0.60000002384185791f, 0.80000001192092896f, 1.0f };

static const float  FaceShade_0[int(6)] = { 0.80000001192092896f, 0.80000001192092896f, 1.0f, 0.5f, 0.89999997615814209f, 0.69999998807907104f };

uint UnpackFace_0(uint faceByte_0)
{

    return faceByte_0 & 7U;
}

uint UnpackAO_0(uint faceByte_1)
{

    return (faceByte_1 >> int(3)) & 3U;
}

float ShadeFor_0(uint faceByte_2)
{
    return FaceShade_0[UnpackFace_0(faceByte_2)] * AoCurve_0[UnpackAO_0(faceByte_2)];
}

struct FSInput_0
{
    float4 Position_0 : SV_POSITION;
    float Shade_0 : COLOR00;
    float2 TileOrigin_0 : TEXCOORD0;
    float2 TileSize_0 : TEXCOORD1;
    float2 Repeat_0 : TEXCOORD2;
    float Opacity_0 : TEXCOORD3;
};

struct VSInput_0
{
    uint4 PosFace_0 : ATTRIB0;
    uint Packed_0 : ATTRIB1;
    float4 ChunkOrigin_0 : ATTRIB2;
};

void VSMain(VSInput_0 input_0, out FSInput_0 vsOut)
{

    VSInput_0 _S1 = input_0;

    uint tile_0 = (input_0.Packed_0) & 255U;
    uint repU_0 = ((input_0.Packed_0) >> int(8)) & 31U;
    uint repV_0 = ((input_0.Packed_0) >> int(13)) & 31U;
    uint cornerU_0 = ((input_0.Packed_0) >> int(18)) & 1U;
    uint cornerV_0 = ((input_0.Packed_0) >> int(19)) & 1U;

    float cols_0 = constants_0.AtlasInfo_0.x;
    float rows_0 = constants_0.AtlasInfo_0.y;
    uint _S2 = uint(cols_0);

    uint _S3 = tile_0 % _S2;

    float _S4 = float(_S3);

    uint _S5 = tile_0 / _S2;

    float2 cell_0 = float2(_S4, float(_S5));

    float t_0 = saturate((constants_0.Fade_0.x - _S1.ChunkOrigin_0.w) / max(constants_0.Fade_0.y, 0.00009999999747379f));

    vsOut.Position_0 = mul(float4(float3(_S1.PosFace_0.xyz) + _S1.ChunkOrigin_0.xyz, 1.0f), constants_0.ViewProjection_0);
    vsOut.Shade_0 = ShadeFor_0(_S1.PosFace_0.w);
    vsOut.TileOrigin_0 = cell_0 / float2(cols_0, rows_0);
    vsOut.TileSize_0 = float2(1.0f / cols_0, 1.0f / rows_0);

    float _S6;
    if(cornerU_0 != 0U)
    {

        _S6 = float(repU_0);

    }
    else
    {

        _S6 = 0.0f;

    }

    float _S7;

    if(cornerV_0 != 0U)
    {

        _S7 = float(repV_0);

    }
    else
    {

        _S7 = 0.0f;

    }

    vsOut.Repeat_0 = float2(_S6, _S7);
    vsOut.Opacity_0 = t_0 * t_0 * (3.0f - 2.0f * t_0);
}
