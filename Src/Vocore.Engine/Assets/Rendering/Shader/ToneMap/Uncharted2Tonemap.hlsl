#include "Rendering/ShaderLib/Core.hlsli"


#pragma EntryVertex vs_main
#pragma EntryFragment fs_main

#pragma RenderPass Surface

DEFINE_TEX2D_SAMPLE(0, _texture); // should be HDR image
DEFINE_UNIFORM(1, _data){
    float A;
    float B;
    float C;
    float D;
    float E;
    float F;
    float W;
    float Exposure;
    float Gamma;
};

float3 Uncharted2Tonemap(float3 x) {
    return ((x*(A*x+C*B)+D*E)/(x*(A*x+B)+D*F)) - E/F;
}


struct Vertex2D {
    float2 position : POSITION;
    float2 uv : TEXCOORD0;
};

struct V2F {
    float4 position : SV_POSITION;
    float2 uv : TEXCOORD0;
};

V2F vs_main(Vertex2D input) {
    V2F output = (V2F)0;
    output.position = float4(input.position, 0.0f, 1.0f);
    output.uv = input.uv;
    return output;
}

float4 fs_main(V2F input) : SV_TARGET {
    float3 hdrColor = max(0, SAMPLE_TEX2D(_texture, input.uv).rgb - 0.004);
    float3 ldrColor = Uncharted2Tonemap(hdrColor * Exposure);

    //white scale
    float3 whiteScale = 1.0 / Uncharted2Tonemap(W);
    ldrColor *= whiteScale;

    //gamma correction
    ldrColor = pow(ldrColor, 1.0 / Gamma);

    float4 color = float4(ldrColor, 1.0);
    return color;
}