#define SLOT(set, bind) [[vk::binding(bind, set)]]


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

SLOT(0,0) cbuffer ViewProjection{
    float4x4 viewProjection;
};

SLOT(1,0) Texture2D image;
SLOT(1,1) SamplerState imageSampler;

[[vk::push_constant]] Constants constants;

V2F MainVS(Vertex2D input) {
    
    float4 position = float4(input.position, 0.0f, 1.0f);
    position = mul(constants.model, position);
    position = mul(viewProjection, position);

    V2F output = (V2F)0;
    output.position = position;
    output.uv = input.uv;
    return output;
}

float4 MainPS(V2F input) : SV_TARGET {
    float value = image.Sample(imageSampler, input.uv).r;

    return float4(value, value, value, 1.0f);
}

