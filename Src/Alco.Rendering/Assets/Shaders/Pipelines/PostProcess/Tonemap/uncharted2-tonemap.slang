// file "uncharted2-tonemap.slang" - module "uncharted2_tonemap"
//
// Slang module converted from Shaders/Pipelines/PostProcess/Tonemap/Uncharted2Tonemap.hlsl (Phase 2 migration).
// Resources are grouped as ParameterBlocks — the compiler assigns each block
// its own descriptor set in declaration order; members resolve by bare field name.

#language slang 2025
module uncharted2_tonemap;

import alco_rendering_core;

public struct PassParams
{
    public Texture2D _texture; // should be HDR image
};

public ParameterBlock<PassParams> _pass;
public struct DataParams
{
    public float A;
    public float B;
    public float C;
    public float D;
    public float E;
    public float F;
    public float W;
    public float Exposure;
    public float Gamma;
};

public ParameterBlock<DataParams> _data;


float3 Uncharted2Tonemap(float3 x) {
    return ((x*(_data.A*x+_data.C*_data.B)+_data.D*_data.E)/(x*(_data.A*x+_data.B)+_data.D*_data.F)) - _data.E/_data.F;
}


struct Vertex {
    float3 position : POSITION;
    float2 uv : TEXCOORD0;
};

struct V2F {
    float4 position : SV_POSITION;
    float2 uv : TEXCOORD0;
};

[shader("vertex")]
V2F MainVS(Vertex input) {
    V2F output = (V2F)0;
    output.position = float4(input.position, 1.0f);
    output.uv = input.uv;
    return output;
}

[shader("fragment")]
float4 MainPS(V2F input) : SV_TARGET {
    float3 hdrColor = max(0, _pass._texture.Sample(_samplers._linearClamp, input.uv).rgb);
    float3 ldrColor = Uncharted2Tonemap(hdrColor * _data.Exposure);

    //white scale
    float3 whiteScale = 1.0 / Uncharted2Tonemap(_data.W);
    ldrColor *= whiteScale;

    //gamma correction
    ldrColor = pow(ldrColor, 1.0 / _data.Gamma);
    ldrColor += OutputDither8Bit(input.position.xy);

    float4 color = float4(ldrColor, 1.0);
    return color;
}