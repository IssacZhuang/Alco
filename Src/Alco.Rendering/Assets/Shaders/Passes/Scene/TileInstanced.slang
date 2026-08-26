// file "TileInstanced.slang" - module "TileInstanced"
//
// Slang module converted from Shaders/Pipelines/Rendering/TileMap/TileInstanced.hlsl (Phase 2 migration).
// Resources are grouped as ParameterBlocks — the compiler assigns each block
// its own descriptor set in declaration order; members resolve by bare field name.

#language slang 2025
module TileInstanced;

import AlcoRendering_Core;
import AlcoRendering_TextureBombing;

struct Constants
{
    float4x4 model;
    int2 size; // the map size
    int currentTileId;
    int reserved; // for memory alignment
    float4 color;
    float blendFactor;
    float tiling;
};

public struct TileData
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

public struct CameraParams
{
    public float4x4 viewProjection;
};

public ParameterBlock<CameraParams> camera;


public struct MaterialParamsData
{
    public Texture2D texture;
};

public ParameterBlock<MaterialParamsData> material;

public struct InstancesParams
{
    public RWStructuredBuffer<TileData> instances;
};

public ParameterBlock<InstancesParams> instances;

public struct TileMapParams
{
    public RWStructuredBuffer<int> tileMap;
};

public ParameterBlock<TileMapParams> tileMap;

[[vk::push_constant]] Constants constants;

int getTileId(int2 tilePos, int defaultValue)
{
    if (tilePos.x < 0 || tilePos.x >= constants.size.x || tilePos.y < 0 || tilePos.y >= constants.size.y)
    {
        return defaultValue;
    }

    return tileMap.tileMap[tilePos.y * constants.size.x + tilePos.x];
}

// The facade/bombing axes are generic value specializations (plan D3): the
// entry points declare <let isFacade>/<let bombing> and the C# owner passes
// "true"/"false" where the retired defines used to be (CreateGraphicsMaterial
// specialization arguments map to the entry points in definition order)
// instead of preprocessor permutations.
[shader("vertex")]
V2F VertexMain<let isFacade : bool>(Vertex input)
{
    V2F output;

    TileData tileData = instances.instances[input.instanceID];

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

    int tileId = getTileId(tilePosInt + checkX, constants.currentTileId);
    if (tileId != constants.currentTileId)
    {
        tilePos.x += check.x * blendFactor;
        uv.x += checkX.x * blendFactor;
    }

    if (isFacade)
    {
        // make it render as a facade
        pos.z = 0.5f - pos.y;
        pos.y -= 1;
    }
    else
    {
        tileId = getTileId(tilePosInt + checkY, constants.currentTileId);
        if (tileId != constants.currentTileId)
        {
            tilePos.y += check.y * blendFactor;
            uv.y -= checkY.y * blendFactor;
        }
    }

    float3 worldPosition = pos + float3(tilePos, 0.0);

    float4 position = mul(constants.model, float4(worldPosition, 1.0));
    position = mul(camera.viewProjection, position);

    output.position = position;
    output.uv = uv;
    output.worldPos = worldPosition.xy;
    return output;
}

[shader("fragment")]
float4 PixelMain<let bombing : bool>(V2F input)
    : SV_TARGET
{
    float2 uv = input.uv;

    float4 color;
    if (bombing)
    {
        color = textureBombing(material.texture, samplers.linearRepeat, input.worldPos, constants.tiling);
    }
    else
    {
        float2 uvFrac = frac(uv * constants.tiling);
        color = material.texture.Sample(samplers.linearRepeat, uvFrac);
    }

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
