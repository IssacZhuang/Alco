#include "Shaders/Libs/Core.hlsli"
#include "Shaders/Libs/Noise.hlsli"
#include "Shaders/Libs/GlobalRenderData.hlsli"

struct Constants
{
    float4x4 model;
    int2 size; // the map size
    int currentTileId;
    int _reserved; // for memory alignment
    float4 color;
    float blendFactor;
};

struct TileData {
    float2 position;
};

struct Vertex {
  float3 position : POSITION;
  float2 uv : TEXCOORD0;
  uint instanceId : SV_INSTANCEID;
  uint vertexId : SV_VERTEXID;
};

struct V2F {
    float4 position : SV_POSITION;
    float2 uv : TEXCOORD0;
    float2 blend : TEXCOORD1;
};

DEFINE_UNIFORM(0, _camera) { float4x4 viewProjection; };

DEFINE_UNIFORM(1, _globalRenderData) { GlobalRenderData globalRenderData; };

DEFINE_TEX2D_SAMPLE(2, _texture);

DEFINE_STORAGE(3, TileData, _instances);

DEFINE_STORAGE(4, int, _tileMap);

PUSH_CONSTANT Constants constants;

int GetTileId(int2 tilePos, int defaultValue)
{
    if (tilePos.x < 0 || tilePos.x >= constants.size.x || tilePos.y < 0 || tilePos.y >= constants.size.y)
    {
        return defaultValue;
    }

    return _tileMap[tilePos.y * constants.size.x + tilePos.x];
}

[shader("vertex")]
V2F VertexMain(Vertex input)
{
    V2F output;
    
    TileData tileData = _instances[input.instanceId];
    
    float3 pos = input.position;
    float2 tilePos = tileData.position;

    float3 worldPosition = pos + float3(tilePos, 0.0);
    
    float4 position = mul(constants.model, float4(worldPosition, 1.0));
    
    position = mul(viewProjection, position);
    output.position = position;
    output.uv = input.uv;

    static const int2 offsetOfCheck[4] = {
        int2(-1, 1),
        int2(1, 1),
        int2(1, -1),
        int2(-1, -1)
    };

    int2 tilePosInt = int2(tilePos);
    int2 check = offsetOfCheck[input.vertexId];
    int2 checkX = int2(check.x, 0);
    int2 checkY = int2(0, check.y);

    output.blend = 0;

    int tileId = GetTileId(tilePosInt + checkX, constants.currentTileId);
    if (tileId != constants.currentTileId)
    {
        output.blend.x = checkX.x * (1+constants.blendFactor);
    }

    tileId = GetTileId(tilePosInt + checkY, constants.currentTileId);
    if (tileId != constants.currentTileId)
    {
        output.blend.y = checkY.y * (1+constants.blendFactor);
    }

    // tileId = GetTileId(tilePosInt + check, constants.currentTileId);
    // if (tileId != constants.currentTileId)
    // {
    //     output.blend = float2(check) * (1 + constants.blendFactor) * (1 - max(output.blend, 1));
    // }

    return output;
}

[shader("pixel")]
float4 PixelMain(V2F input)
    : SV_TARGET
{
    float4 color = SAMPLE_TEX2D(_texture, input.uv) * constants.color;
    
    // Water ripple effect using noise
    float time = globalRenderData.time * 0.8;

    float2 overflow = abs(input.blend) - 1;
    float maxOverflow = max(overflow.x, overflow.y);

    float blendFactor = constants.blendFactor;

    if(blendFactor > 0)
    {
        float t = 1.0 - saturate(maxOverflow / constants.blendFactor);
        // lerp to white
        color = lerp(float4(1, 1, 1, 1), color, t);
    }

    return color;
} 