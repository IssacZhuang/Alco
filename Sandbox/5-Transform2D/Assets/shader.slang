// file "shader.slang" - module "sandbox5_shader"
//
// Slang module converted from Shader.hlsl (Phase 4 migration): the low-level
// sandbox compiles it through the native slang module API. The SLOT macro
// became set-scoped cbuffer blocks (bindings are compiler-assigned).

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

cbuffer ViewProjection : register(b0, space0) {
    float4x4 viewProjection;
};

cbuffer Model : register(b0, space1) {
    float4x4 model;
};

cbuffer _material : register(b0, space2)
{
    Texture2D image;
    SamplerState imageSampler;
};

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
