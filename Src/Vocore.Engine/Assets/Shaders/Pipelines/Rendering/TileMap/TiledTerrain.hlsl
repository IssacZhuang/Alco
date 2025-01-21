#include "Shaders/Libs/Core.hlsli"

struct Vertex {
  float3 position : POSITION;
  float2 uv : TEXCOORD0;
  uint instanceId : SV_INSTANCEID;
};

struct V2F {
  float4 position : SV_POSITION;
  float4 color : COLOR;
  float2 uv : TEXCOORD0;
  uint instanceId : TEXCOORD1;
};

struct Constants{
    float4x4 model;
    int2 size;
};

struct SpriteData{
    float4 uvRect;
    float2 meshScale;
    float2 uvScale;
    float blendFactor;
    float blendPriority;
    float2 _reserved;//reserved for memory alignment
};



DEFINE_UNIFORM(0, _camera) { float4x4 viewProjection; };

DEFINE_TEX2D_SAMPLE(1, _texture);

DEFINE_STORAGE(2, SpriteData, _spriteData);

DEFINE_STORAGE(3, float4, _colorData);

DEFINE_STORAGE(4, uint, _tileIdData);

PUSH_CONSTANT Constants constants;

float4 SampleTile(uint tileId, float2 vertexUV, out float blendPriority)
{
    SpriteData sprite = _spriteData[tileId];
    float2 uv = frac(vertexUV * sprite.uvScale);
    uv = uv * sprite.uvRect.zw + sprite.uvRect.xy;
    blendPriority = sprite.blendPriority;
    return SAMPLE_TEX2D(_texture, uv);
}

[shader("vertex")]
V2F VertexMain(Vertex input)
{
    float4 color = _colorData[input.instanceId];
    uint tileId = _tileIdData[input.instanceId];
    SpriteData sprite = _spriteData[tileId];

    V2F output;
    float offsetX = (input.instanceId % constants.size.x) - (constants.size.x-1) *0.5f;
    float offsetY = (input.instanceId / constants.size.x) - (constants.size.y-1) *0.5f;

    float3 pos2D = input.position * float3(sprite.meshScale, 1.0f);

    float4 position = float4(pos2D, 1);
    position.xy += float2(offsetX, -offsetY);
    position = mul(constants.model, position);
    position = mul(viewProjection, position);
    output.position = position;

    output.color = color;
    output.uv = input.uv;
    output.instanceId = input.instanceId;

    return output;
}


[shader("pixel")]
float4 PixelMain(V2F input) : SV_TARGET
{
    uint tileId_center = _tileIdData[input.instanceId];
    uint tileId_left = _tileIdData[input.instanceId - 1];
    uint tileId_right = _tileIdData[input.instanceId + 1];
    uint tileId_top = _tileIdData[input.instanceId - constants.size.x];
    uint tileId_bottom = _tileIdData[input.instanceId + constants.size.x];

    SpriteData sprite= _spriteData[tileId_center];

    float blendPriorityCenter;
    float blendPriorityLeft;
    float blendPriorityRight;
    float blendPriorityTop;
    float blendPriorityBottom;

    float4 colorCenter = SampleTile(tileId_center, input.uv, blendPriorityCenter) * input.color;
    float4 colorLeft = SampleTile(tileId_left, input.uv, blendPriorityLeft) * input.color;
    float4 colorRight = SampleTile(tileId_right, input.uv, blendPriorityRight) * input.color;
    float4 colorTop = SampleTile(tileId_top, input.uv, blendPriorityTop) * input.color;
    float4 colorBottom = SampleTile(tileId_bottom, input.uv, blendPriorityBottom) * input.color;

    float4 finalColor = colorCenter;
    float blendWidth = sprite.blendFactor;

    // Left edge blend
    
    float tLeft = saturate(input.uv.x / blendWidth);
    if(blendPriorityLeft > blendPriorityCenter)
    {
        finalColor = lerp(colorLeft, finalColor, tLeft);
    }

    // Right edge blend
    float tRight = saturate((1 - input.uv.x) / blendWidth);
    if(blendPriorityRight > blendPriorityCenter)
    {
        finalColor = lerp(colorRight, finalColor, tRight);
    }

    // Top edge blend
    float tTop = saturate(input.uv.y / blendWidth);
    if(blendPriorityTop > blendPriorityCenter)
    {
        finalColor = lerp(colorTop, finalColor, tTop);
    }

    // Bottom edge blend
    float tBottom = saturate((1 - input.uv.y) / blendWidth);
    if(blendPriorityBottom > blendPriorityCenter)
    {
        finalColor = lerp(colorBottom, finalColor, tBottom);
    }

    return finalColor;
}

