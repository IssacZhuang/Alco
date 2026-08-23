// file "shader.slang" - module "sandbox11_shader"
//
// Slang module converted from Assets/Shader.hlsl (Phase 2 migration): the
// asset loader derives the module from the file name and the ShaderSystem
// compiles it through the native slang module API.

#language slang 2025
module sandbox11_shader;

[[vk::binding(0,0)]] cbuffer Constants{
    float3 color;
};

[[vk::binding(0,1)]] Texture2D image;
[[vk::binding(1,1)]] SamplerState imageSampler;

struct VertexInput {
    float3 position : POSITION0;
    float3 color : COLOR0;
    float2 texCoord : TEXCOORD0;
};

struct VertexOutput {
    float4 clip_position : SV_POSITION;
    float2 texCoord : TEXCOORD0;
};

[shader("vertex")]
VertexOutput MainVS(VertexInput model) {
    VertexOutput v2f;
    v2f.clip_position = float4(model.position, 1);
    v2f.texCoord = model.texCoord;
    return v2f;
}


[shader("fragment")]
float4 MainPS(VertexOutput input) : SV_Target0 {
    float4 result =  image.Sample(imageSampler, input.texCoord);
    // inverse gamma correction
    result = pow(result, float4(2.2, 2.2, 2.2, 2.2));
    return result;
}
