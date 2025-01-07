#include "Shaders/Libs/Core.hlsli"

struct Vertex2D {
  float2 position : POSITION;
  float2 uv : TEXCOORD0;
  uint instanceId : SV_INSTANCEID;
};

struct V2F {
  float4 position : SV_POSITION;
  float2 uv : TEXCOORD0;
  uint instanceId : TEXCOORD1;
};

struct Constants{
    float4x4 model;
};

struct SpriteData{
    float4 uvRect;
    float2 scale;
};

struct TileData{
    float4 color;
    int2 position;
    int spriteIndex;
};



DEFINE_UNIFORM(0, _camera) { float4x4 viewProjection; };

DEFINE_STORAGE(1, SpriteData, _spriteData);

DEFINE_STORAGE(2, TileData, _tileData);

PUSH_CONSTANT Constants constants;

[shader("vertex")]
V2F VertexMain(Vertex2D input)
{
    V2F output;

    output.position = mul(constants.model, float4(input.position, 0, 1));

    TileData tile = _tileData[input.instanceId];
    SpriteData sprite = _spriteData[tile.spriteIndex];

    output.uv = input.uv * sprite.scale + sprite.uvRect.xy;
    output.instanceId = input.instanceId;

    return output;
}


[shader("pixel")]
float4 PixelMain(V2F input) : SV_TARGET
{
    
    
    
    return float4(1, 1, 1, 1);
}
