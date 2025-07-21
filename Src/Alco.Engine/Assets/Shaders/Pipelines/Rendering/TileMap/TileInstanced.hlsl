#include "Shaders/Libs/Core.hlsli"

struct Constants {
  float4x4 model;
  float4 size;//the map size
};

struct TileData {
    float2 position;
};

struct Vertex {
  float3 position : POSITION;
  float2 uv : TEXCOORD0;
  uint instanceId : SV_INSTANCEID;
};

struct V2F {
    float4 position : SV_POSITION;
    float2 uv : TEXCOORD0;
};

DEFINE_UNIFORM(0, _camera) { float4x4 viewProjection; };

DEFINE_TEX2D_SAMPLE(1, _texture);

DEFINE_STORAGE(2, TileData, _instances);

[shader("vertex")]
V2F VertexMain(Vertex input)
{
    V2F output;
    
    TileData tileData = _instances[input.instanceId];
    
    float3 worldPosition = input.position + float3(tileData.position, 0.0);
    
    float4 modelPosition = mul(viewProjection, float4(worldPosition, 1.0));
    
    output.position = modelPosition;
    output.uv = input.uv;
    
    return output;
}

[shader("pixel")]
float4 PixelMain(V2F input)
    : SV_TARGET
{
    float4 color = SAMPLE_TEX2D(_texture, input.uv);
    return color;
}