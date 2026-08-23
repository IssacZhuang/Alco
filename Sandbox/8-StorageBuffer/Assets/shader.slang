// file "shader.slang" - module "sandbox8_shader"
//
// Slang module converted from Shader.hlsl (Phase 4 migration, copied from 7-Instancing): the low-level
// sandbox compiles it through the native slang module API. The SLOT/readonly
// macros became explicit decorations (read-only-ness is the cbuffer's own).

#language slang 2025
module sandbox8_shader;

#define MAX_INSTANCE_COUNT 500

struct Vertex2D{
    float2 position : POSITION;
    float2 uv : TEXCOORD0;
    uint instanceID : SV_VulkanInstanceID;
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

cbuffer _material : register(b0, space1)
{
    Texture2D image;
    SamplerState imageSampler;
};

cbuffer PositionsBuffer : register(b0, space2) {
    float4 Positions[MAX_INSTANCE_COUNT];
}

[[vk::push_constant]] Constants constants;

[shader("vertex")]
V2F MainVS(Vertex2D input) {
    float4 position = float4(input.position+Positions[input.instanceID].xy, 0.0f, 1.0f);
    position = mul(constants.model, position);
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
