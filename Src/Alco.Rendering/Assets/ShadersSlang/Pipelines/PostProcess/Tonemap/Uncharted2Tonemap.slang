// file "Uncharted2Tonemap.slang" - module "uncharted2_tonemap"
//
// Slang module converted from Shaders/Pipelines/PostProcess/Tonemap/Uncharted2Tonemap.hlsl (Phase 2 migration).
// DEFINE_* macros became explicit set/binding declarations; bindings within a
// set are assigned in the original declaration order and are compiler-owned.

#language slang 2025
module uncharted2_tonemap;

import alco_rendering_core;

// Transitional sampling shorthands: the preprocessor does not cross module
// boundaries, so the old Core.hlsli macros live here until Phase 3 deletes the
// name##Sampler convention outright.
#define SAMPLE_TEX2D(textureName, uv) textureName.Sample(textureName##Sampler, uv)
#define SAMPLE_TEX2D_LEVEL(textureName, uv, mip) textureName.SampleLevel(textureName##Sampler, uv, mip)
#define SAMPLE_TEX3D_LEVEL(textureName, uvw, mip) textureName.SampleLevel(textureName##Sampler, uvw, mip)
#define GET_PIXEL_TEX2D(textureName, position) textureName.Load(int3(position, 0))
#define LOAD_TEX3D(textureName, coord, mip) textureName.Load(int4(coord, mip))
#define SAMPLE_TEX2D_DEPTH_CMP(textureName, uv, compareDepth) textureName.SampleCmpLevelZero(textureName##Sampler, uv, compareDepth)

[[vk::binding(0, 0)]] Texture2D _texture;
[[vk::binding(1, 0)]] SamplerState _textureSampler; // should be HDR image
[[vk::binding(0, 1)]] cbuffer _data {
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
    float3 hdrColor = max(0, SAMPLE_TEX2D(_texture, input.uv).rgb);
    float3 ldrColor = Uncharted2Tonemap(hdrColor * Exposure);

    //white scale
    float3 whiteScale = 1.0 / Uncharted2Tonemap(W);
    ldrColor *= whiteScale;

    //gamma correction
    ldrColor = pow(ldrColor, 1.0 / Gamma);
    ldrColor += OutputDither8Bit(input.position.xy);

    float4 color = float4(ldrColor, 1.0);
    return color;
}