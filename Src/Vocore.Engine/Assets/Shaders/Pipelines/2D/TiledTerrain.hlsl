#include "Shaders/Libs/Core.hlsli"

struct Vertex2D {
  float2 position : POSITION;
  float2 uv : TEXCOORD0;
  uint instanceId : SV_INSTANCEID;
};

struct V2F {
  float4 position : SV_POSITION;
  float4 color : COLOR;
  float4 uvRect : TEXCOORD0;
  float2 uv : TEXCOORD1;
  uint instanceId : TEXCOORD2;
};

struct Constants{
    float4x4 model;
    int2 size;
};

struct SpriteData{
    float4 uvRect;
    float2 meshScale;
    float2 uvScale;
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
  float4 color = _colorData[input.instanceId];
    uint tileId = _tileIdData[input.instanceId];
    SpriteData sprite = _spriteData[tileId];

    V2F output;
    float offsetX = (input.instanceId % constants.size.x) - (constants.size.x-1) *0.5f;
    float offsetY = (input.instanceId / constants.size.x) - (constants.size.y-1) *0.5f;

    float2 pos2D = input.position * sprite.meshScale;

    float4 position = float4(pos2D, 0, 1);
    position.xy += float2(offsetX, -offsetY);
    position = mul(constants.model, position);
    position = mul(viewProjection, position);
    output.position = position;

    output.color = color;
    output.uv = input.uv * sprite.uvScale;//sprite.uvRect.zw + sprite.uvRect.xy;
    output.uvRect = sprite.uvRect;
    output.instanceId = input.instanceId;

    return output;
}


[shader("pixel")]
float4 PixelMain(V2F input) : SV_TARGET
{
    float2 uv = frac(input.uv);
    uv = uv * input.uvRect.zw + input.uvRect.xy;

    return SAMPLE_TEX2D(_texture, uv) * input.color;
}
