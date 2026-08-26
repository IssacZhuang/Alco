// file "TileWaterInstanced.slang" - module "TileWaterInstanced"
//
// Resources are grouped as ParameterBlocks — the compiler assigns each block
// its own descriptor set in declaration order; members resolve by bare field name.

#language slang 2025
module TileWaterInstanced;

import AlcoRendering_Core;
import AlcoRendering_GlobalRenderData;
import AlcoRendering_Noise;
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

public struct TileData {
  float2 position;
};

struct Vertex {
  float3 position : POSITION;
  float2 uv : TEXCOORD0;
  uint instanceID : SV_VulkanInstanceID;
  uint vertexId : SV_VulkanVertexID;
};

struct V2F {
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

public struct GlobalRenderDataParams
{
    public GlobalRenderData globalRenderData;
};

public ParameterBlock<GlobalRenderDataParams> globalRenderData;

[[vk::push_constant]] Constants constants;

int getTileId(int2 tilePos, int defaultValue) {
  if (tilePos.x < 0 || tilePos.x >= constants.size.x || tilePos.y < 0 ||
      tilePos.y >= constants.size.y) {
    return defaultValue;
  }

  return tileMap.tileMap[tilePos.y * constants.size.x + tilePos.x];
}

[shader("vertex")] V2F VertexMain(Vertex input) {
  V2F output;

  TileData tileData = instances.instances[input.instanceID];

  float3 pos = input.position;
  float2 tilePos = tileData.position;
  int2 tilePosInt = int2(tilePos);

  float blendFactor = constants.blendFactor;

  float2 uv = input.uv;

  static const int2 offsetOfCheck[4] = {int2(-1, 1), int2(1, 1), int2(1, -1),
                                        int2(-1, -1)};

  int2 check = offsetOfCheck[input.vertexId];
  int2 checkX = int2(check.x, 0);
  int2 checkY = int2(0, check.y);

  int tileId = getTileId(tilePosInt + checkX, constants.currentTileId);
  if (tileId != constants.currentTileId) {
    tilePos.x += check.x * blendFactor;
    uv.x += checkX.x * blendFactor;
  }

  tileId = getTileId(tilePosInt + checkY, constants.currentTileId);
  if (tileId != constants.currentTileId) {
    tilePos.y += check.y * blendFactor;
    uv.y -= checkY.y * blendFactor;
  }

  float3 worldPosition = pos + float3(tilePos, 0.0);

  float4 position = mul(constants.model, float4(worldPosition, 1.0));
  position = mul(camera.viewProjection, position);

  output.position = position;
  output.uv = uv;
  output.worldPos = worldPosition.xy;
  return output;
}

    [shader("fragment")] float4 PixelMain(V2F input)
    : SV_TARGET {
  float2 uv = input.uv;

  float2 uvFrac = frac(uv * constants.tiling);
  float4 color = material.texture.Sample(samplers.linearRepeat, uvFrac);

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
    }

    // Shimmering water effect - use sin and time as noise coordinates
    float time = globalRenderData.globalRenderData.time;
    float2 worldPos = input.worldPos;

    // Create noise state for FastNoiseLite
    fnl_state noiseState = fnlCreateState(12345);
    noiseState.frequency = 0.03;

    float scale = 30;

    // Generate noise coordinates using sin functions and time
    float2 noiseCoord = float2(
        worldPos.x * scale,
        worldPos.y * scale
    );

    // Sample noise using the generated coordinates
    float noise = fnlGetNoise2D(noiseState, noiseCoord.x, noiseCoord.y);

    // Normalize to 0-1 range
    noise = (noise + 1.0) * 0.5;

    // Set threshold for shimmer effect
    float shimmerThreshold = 0.91;

    float speed = 12;

    noiseState.seed = 437;
    float noise2 = fnlGetNoise2D(noiseState, noiseCoord.x * 0.5 - time * speed, noiseCoord.y * 7 );

    float sinT = (noise2 +1)*0.2;

    float t = noise - shimmerThreshold - sinT;
    t = max(0, t) / (1-shimmerThreshold);
    color.rgb = lerp(color.rgb, color.rgb *2, t);

    noiseState.seed = 111;
    // Create ripple coordinates with time animation
    float2 rippleCoord = float2(
        worldPos.x + time ,
        worldPos.y + time
    );

    return color;
}
