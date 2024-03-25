#include "Rendering/ShaderLib/Core.hlslinc"

#pragma EntryVertex vs_main
#pragma EntryFragment fs_main

#define MAX_INSTANCE_COUNT 200

struct Vertex2D
{
    float2 position : POSITION;
    float2 uv : TEXCOORD0;
    uint instanceId : SV_INSTANCEID;
};

struct V2F
{
    float4 position : SV_POSITION;
    float2 uv : TEXCOORD0;
    uint instanceId: TEXCOORD1;
};

struct Constants
{
    float4x4 model;
};

struct TextData{
    float4 uvRect;
    float4 color;
    float2 offset;
    float2 size;
};


SLOT(0, 0)
cbuffer ViewProjection
{
    float4x4 viewProjection;
};

SLOT(1, 0)
Texture2D fontAtlas;
SLOT(1, 1)
SamplerState fontAtlasSampler;

SLOT(2, 0)
cbuffer TextBuffer
{
    TextData Data[MAX_INSTANCE_COUNT];
}

PUSH_CONSTANT Constants constants;

V2F vs_main(Vertex2D input)
{
    //float4 position = float4(input.position + Positions[input.instanceId].xy, 0.0f, 1.0f);
    float2 vertexPos = input.position * Data[input.instanceId].size;
    float4 position = float4(vertexPos + Data[input.instanceId].offset, 0.0f, 1.0f);
    position = mul(constants.model, position);
    position = mul(viewProjection, position);

    V2F output = (V2F)0;
    output.position = position;
    output.uv = input.uv;
    output.instanceId = input.instanceId;
    return output;
}

float4 fs_main(V2F input) : SV_TARGET
{
    float2 uv = input.uv * Data[input.instanceId].uvRect.zw + Data[input.instanceId].uvRect.xy;
    float r = fontAtlas.Sample(fontAtlasSampler, uv).r;
    return float4(r,r,r,r)* Data[input.instanceId].color;
}
