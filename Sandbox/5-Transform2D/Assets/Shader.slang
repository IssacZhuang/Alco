// file "Shader.slang" - module "sandbox5_shader"
//
// Slang module converted from Shader.hlsl (Phase 4 migration): the low-level
// sandbox compiles it through the native slang module API. The SLOT macro
// became explicit vk::binding decorations.

#language slang 2025
module sandbox5_shader;

struct Vertex2D{
    float2 position : POSITION;
    float2 uv : TEXCOORD0;
};

struct V2F{
    float4 position : SV_POSITION;
    float2 uv : TEXCOORD0;
};

struct Constants{
    float4x4 model;
};

[[vk::binding(0, 0)]] cbuffer ViewProjection{
    float4x4 viewProjection;
};

[[vk::binding(0, 1)]] cbuffer Model{
    float4x4 model;
};

[[vk::binding(0, 2)]] Texture2D image;
[[vk::binding(1, 2)]] SamplerState imageSampler;

//[[vk::push_constant]] Constants constants;

[shader("vertex")]
V2F MainVS(Vertex2D input) {

    float4 position = float4(input.position, 0.0f, 1.0f);
    position = mul(model, position);
    position = mul(viewProjection, position);

    V2F output = (V2F)0;
    output.position = position;
    output.uv = input.uv;
    return output;
}

[shader("fragment")]
float4 MainPS(V2F input) : SV_TARGET {
    return image.Sample(imageSampler, input.uv);
}
