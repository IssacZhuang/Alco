#include "Shaders/Libs/Core.hlsli"
#include "Shaders/Libs/TextureBombing.hlsli"

struct Constants
{
    float4x4 model;
    int2 size; // the map size
    int currentTileId;
    int _reserved; // for memory alignment
    float4 color;
    float blendFactor;
};

struct TileData
{
    float2 position;
};

struct Vertex
{
    float3 position : POSITION;
    float2 uv : TEXCOORD0;
    uint instanceId : SV_INSTANCEID;
    uint vertexId : SV_VERTEXID;
};

struct V2F
{
    float4 position : SV_POSITION;
    float2 uv : TEXCOORD0;
    float2 worldPos : TEXCOORD1;
};

DEFINE_UNIFORM(0, _camera)
{
    float4x4 viewProjection;
};

DEFINE_TEX2D_SAMPLE(1, _texture);

DEFINE_STORAGE(2, TileData, _instances);

DEFINE_STORAGE(3, int, _tileMap);

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
    int2 tilePosInt = int2(tilePos);

    float blendFactor = constants.blendFactor;

    float2 uv = input.uv;

    static const int2 offsetOfCheck[4] = {
        int2(-1, 1),
        int2(1, 1),
        int2(1, -1),
        int2(-1, -1)
    };

    int2 check = offsetOfCheck[input.vertexId];
    int2 checkX = int2(check.x, 0);
    int2 checkY = int2(0, check.y);

    int tileId = GetTileId(tilePosInt + checkX, constants.currentTileId);
    if (tileId != constants.currentTileId)
    {
        tilePos.x += check.x * blendFactor;
        uv.x += checkX.x * blendFactor;
    }

#if defined(IS_FACADE)
    // make it render as a facade
    pos.z = 0.5f - pos.y;
    pos.y -= 1;
#else
    tileId = GetTileId(tilePosInt + checkY, constants.currentTileId);
    if (tileId != constants.currentTileId)
    {
        tilePos.y += check.y * blendFactor;
        uv.y -= checkY.y * blendFactor;
    }
#endif

    float3 worldPosition = pos + float3(tilePos, 0.0);

    float4 position = mul(constants.model, float4(worldPosition, 1.0));
    position = mul(viewProjection, position);

    output.position = position;
    output.uv = uv;
    output.worldPos = worldPosition.xy;
    return output;
}

[shader("pixel")]
float4 PixelMain(V2F input)
    : SV_TARGET
{
    float2 uv = input.uv;

#if defined(TEXTURE_BOMBING)
    float4 color = TextureBombing(_texture, _textureSampler, input.worldPos);
#else
    float2 uvFrac = frac(uv);
    float4 color = SAMPLE_TEX2D(_texture, uvFrac);
#endif

    color *= constants.color;

    float blendFactor = constants.blendFactor;

    float2 uvOverflow = abs(uv - clamp(uv, 0.0, 1.0));
    float maxOverflow = max(uvOverflow.x, uvOverflow.y);
    float alpha = 1.0;
    if (blendFactor > 0.0 && maxOverflow > 0)
    {
        float t = saturate(maxOverflow / blendFactor);
        alpha = 1.0 - t;
        color.a *= alpha;
        //lerp to white
        //color.rgb = lerp(color.rgb, float3(1.0, 1.0, 1.0), t);
    }

    return color;
}
