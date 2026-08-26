// file "Uncharted2Tonemap.slang" - module "Uncharted2Tonemap"
//
// Resources are grouped as ParameterBlocks — the compiler assigns each block
// its own descriptor set in declaration order; members resolve by bare field name.

#language slang 2025
module Uncharted2Tonemap;

import AlcoRendering_Core;

public struct PassParams
{
    public Texture2D texture; // should be HDR image
};

public ParameterBlock<PassParams> pass;
public struct DataParams
{
    public float a;
    public float b;
    public float c;
    public float d;
    public float e;
    public float f;
    public float w;
    public float exposure;
    public float gamma;
};

public ParameterBlock<DataParams> data;


float3 uncharted2Tonemap(float3 x) {
    return ((x*(data.a*x+data.c*data.b)+data.d*data.e)/(x*(data.a*x+data.b)+data.d*data.f)) - data.e/data.f;
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
    float3 hdrColor = max(0, pass.texture.Sample(samplers.linearClamp, input.uv).rgb);
    float3 ldrColor = uncharted2Tonemap(hdrColor * data.exposure);

    //white scale
    float3 whiteScale = 1.0 / uncharted2Tonemap(data.w);
    ldrColor *= whiteScale;

    //gamma correction
    ldrColor = pow(ldrColor, 1.0 / data.gamma);
    ldrColor += outputDither8Bit(input.position.xy);

    float4 color = float4(ldrColor, 1.0);
    return color;
}