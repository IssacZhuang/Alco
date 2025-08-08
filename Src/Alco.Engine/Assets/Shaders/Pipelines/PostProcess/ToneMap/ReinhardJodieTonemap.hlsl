#include "Shaders/Libs/Core.hlsli"

DEFINE_TEX2D_SAMPLE(0, _texture);
DEFINE_UNIFORM(1, _data){
    float Exposure;
    float Gamma;
};

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

[shader("pixel")]
float4 MainPS(V2F input) : SV_TARGET {
    float3 LUMA = float3(0.2126, 0.7152, 0.0722);
    float3 hdr = SAMPLE_TEX2D(_texture, input.uv).rgb * Exposure;

    float l = max(0.0, dot(hdr, LUMA));
    float3 mapped = hdr / (1.0 + hdr);
    float lt = l / (1.0 + l);
    float scale = (l > 1e-6) ? (lt / l) : 0.0;
    float3 ldr = mapped * scale;

    ldr = pow(saturate(ldr), 1.0 / Gamma);
    return float4(ldr, 1.0);
}

