// file "tile-instanced.slang" - module "tile_instanced"
//
// Slang module converted from Shaders/Pipelines/Rendering/TileMap/TileInstanced.hlsl (Phase 2 migration).
// DEFINE_* macros became explicit set/binding declarations; bindings within a
// set are assigned in the original declaration order and are compiler-owned.

#language slang 2025
module tile_instanced;

import alco_rendering_core;
import alco_rendering_texture_bombing;

// Transitional sampling shorthands: the preprocessor does not cross module
// boundaries, so the old Core.hlsli macros live here until Phase 3 deletes the
// name##Sampler convention outright.
#define SAMPLE_TEX2D(textureName, uv) textureName.Sample(textureName##Sampler, uv)
#define SAMPLE_TEX2D_LEVEL(textureName, uv, mip) textureName.SampleLevel(textureName##Sampler, uv, mip)
#define SAMPLE_TEX3D_LEVEL(textureName, uvw, mip) textureName.SampleLevel(textureName##Sampler, uvw, mip)
#define GET_PIXEL_TEX2D(textureName, position) textureName.Load(int3(position, 0))
#define LOAD_TEX3D(textureName, coord, mip) textureName.Load(int4(coord, mip))
#define SAMPLE_TEX2D_DEPTH_CMP(textureName, uv, compareDepth) textureName.SampleCmpLevelZero(textureName##Sampler, uv, compareDepth)

struct Constants
{
    float4x4 model;
    int2 size; // the map size
    int currentTileId;
    int _reserved; // for memory alignment
    float4 color;
    float blendFactor;
    float tiling;
};

struct TileData
{
    float2 position;
};

struct Vertex
{
    float3 position : POSITION;
    float2 uv : TEXCOORD0;
    uint instanceID : SV_VulkanInstanceID;
    uint vertexId : SV_VulkanVertexID;
};

struct V2F
{
    float4 position : SV_POSITION;
    float2 uv : TEXCOORD0;
    float2 worldPos : TEXCOORD1;
};

[[vk::binding(0, 0)]] cbuffer _camera {
    float4x4 viewProjection;
};


[[vk::binding(0, 1)]] Texture2D _texture;
[[vk::binding(1, 1)]] SamplerState _textureSampler;

[[vk::binding(0, 2)]] RWStructuredBuffer<TileData> _instances;

[[vk::binding(0, 3)]] RWStructuredBuffer<int> _tileMap;

[[vk::push_constant]] Constants constants;

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

    TileData tileData = _instances[input.instanceID];

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

[shader("fragment")]
float4 PixelMain(V2F input)
    : SV_TARGET
{
    float2 uv = input.uv;

#if defined(TEXTURE_BOMBING)
    float4 color = TextureBombing(_texture, _textureSampler, input.worldPos, constants.tiling);
#else
    float2 uvFrac = frac(uv * constants.tiling);
    float4 color = SAMPLE_TEX2D(_texture, uvFrac);
#endif

    float blendFactor = constants.blendFactor;

    float2 uvOverflow = abs(uv - clamp(uv, 0.0, 1.0));
    float maxOverflow = max(uvOverflow.x, uvOverflow.y);
    float alpha = 1.0;
    if (blendFactor > 0.0 && maxOverflow > 0)
    {
        float t = maxOverflow - (sin((input.worldPos.x *7 + input.worldPos.y *7) + 1) * 0.015);
        alpha = 1.0 - saturate(t / blendFactor);
        color.a *= alpha;
    }

    

    return color * constants.color;
}
