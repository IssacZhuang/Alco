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
    // Define offsets for 3x3 neighborhood
    static const int2 offsets[9] = {
        int2(-1, -1), int2(0, -1), int2(1, -1), // top row
        int2(-1, 0), int2(0, 0), int2(1, 0),    // middle row
        int2(-1, 1), int2(0, 1), int2(1, 1)     // bottom row
    };

    // Sample all neighbors
    float4 colors[9];
    float priorities[9];

    [unroll]
    for (int i = 0; i < 9; i++)
    {
        int neighborIndex = input.instanceId + offsets[i].x + offsets[i].y * constants.size.x;
        uint tileId = _tileIdData[neighborIndex];
        colors[i] = SampleTile(tileId, input.uv, priorities[i]) * input.color;
    }

    float4 finalColor = colors[4]; // Center color
    float centerPriority = priorities[4];
    float blendFactor = _spriteData[_tileIdData[input.instanceId]].blendFactor;
    float cornerBlendFactor = blendFactor ; // sqrt(2)

    // Pre-calculate reciprocals
    float invBlendFactor = 1.0 / blendFactor;
    float invCornerBlendFactor = 1.0 / cornerBlendFactor;

    // Define blend weights for each neighbor
    float2 uv = input.uv;
    float weights[9] = {
        saturate((uv.x + uv.y) * invCornerBlendFactor),            // top-left
        saturate(uv.y * invBlendFactor),                           // top
        saturate(((1 - uv.x) + uv.y) * invCornerBlendFactor),      // top-right
        saturate(uv.x * invBlendFactor),                           // left
        1.0,                                                       // center
        saturate((1 - uv.x) * invBlendFactor),                     // right
        saturate((uv.x + (1 - uv.y)) * invCornerBlendFactor),      // bottom-left
        saturate((1 - uv.y) * invBlendFactor),                     // bottom
        saturate(((1 - uv.x) + (1 - uv.y)) * invCornerBlendFactor) // bottom-right
    };

    // Apply blending for all neighbors in a single loop
    [unroll]
    for (int j = 0; j < 9; j++)
    {
        if (j != 4 && priorities[j] > centerPriority) // Skip center tile
        {
            finalColor = lerp(colors[j], finalColor, weights[j]);
        }
    }

    return finalColor;
}

