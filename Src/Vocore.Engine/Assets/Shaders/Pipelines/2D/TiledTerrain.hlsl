#include "Shaders/Libs/Core.hlsli"

struct Vertex2D {
  float2 position : POSITION;
  float2 uv : TEXCOORD0;
  uint instanceId : SV_INSTANCEID;
};

struct V2F {
  float4 position : SV_POSITION;
  float2 uv : TEXCOORD0;
  float4 color : COLOR;
  uint instanceId : TEXCOORD1;
};

struct Constants{
    float4x4 model;
    int2 size;
};

struct SpriteData{
    float4 uvRect;
    float4 scale;
};



DEFINE_UNIFORM(0, _camera) { float4x4 viewProjection; };

DEFINE_TEX2D_SAMPLE(1, _texture);

DEFINE_STORAGE(2, SpriteData, _spriteData);

DEFINE_STORAGE(3, float4, _colorData);

DEFINE_STORAGE(4, uint, _tileIdData);
PUSH_CONSTANT Constants constants;

[shader("vertex")]
V2F VertexMain(Vertex2D input)
{
    V2F output;
    int x = input.instanceId % constants.size.x;
    int y = input.instanceId / constants.size.x;

    float4 position = float4(input.position, 0, 1);
    position.xy += float2(x, y);
    position = mul(constants.model, position);
    position = mul(viewProjection, position);
    output.position = position;

    float4 color = _colorData[input.instanceId];
    uint tileId = _tileIdData[input.instanceId];
    SpriteData sprite = _spriteData[tileId];

    output.color = color;
    output.uv = input.uv * sprite.uvRect.zw + sprite.uvRect.xy;
    output.instanceId = input.instanceId;

    return output;
}


[shader("pixel")]
float4 PixelMain(V2F input) : SV_TARGET
{
    return SAMPLE_TEX2D(_texture, input.uv) * input.color;
}
