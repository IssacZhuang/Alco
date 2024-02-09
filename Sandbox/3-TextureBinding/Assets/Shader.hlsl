[[vk::binding(0,0)]] cbuffer Constants{
    float3 color;
};

[[vk::binding(0,1)]] Texture2D image;
[[vk::binding(1,1)]] SamplerState imageSampler;

struct VertexInput {
    uint instanceID : SV_InstanceID;
    float3 position : POSITION0;
    float3 color : COLOR0;
    float2 texCoord : TEXCOORD0;
};

struct VertexOutput {
    float4 clip_position : SV_POSITION;
    float3 color : COLOR0;
    float2 texCoord : TEXCOORD0;
};

VertexOutput vs_main(VertexInput model) {
    VertexOutput v2f;
    v2f.color = model.color;
    v2f.clip_position = float4(model.position, 1);
    v2f.texCoord = model.texCoord;
    return v2f;
}

float4 fs_main(VertexOutput input) : SV_Target0 {
    float4 result = float4(color, 1.0) * image.Sample(imageSampler, input.texCoord);
    // inverse gamma correction
    result = pow(result, float4(2.2, 2.2, 2.2, 2.2));
    return result;
}
