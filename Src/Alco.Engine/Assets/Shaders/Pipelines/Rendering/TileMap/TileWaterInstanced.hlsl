#include "Shaders/Libs/Core.hlsli"
#include "Shaders/Libs/Noise.hlsli"
#include "Shaders/Libs/GlobalRenderData.hlsli"

struct Constants {
  float4x4 model;
  float4 size;//the map size
};

struct TileData {
    float2 position;
};

struct Vertex {
  float3 position : POSITION;
  float2 uv : TEXCOORD0;
  uint instanceId : SV_INSTANCEID;
};

struct V2F {
    float4 position : SV_POSITION;
    float2 uv : TEXCOORD0;
    float4 worldPosition : TEXCOORD1;
};

DEFINE_UNIFORM(0, _camera) { float4x4 viewProjection; };

DEFINE_UNIFORM(1, _globalRenderData) { GlobalRenderData globalRenderData; };

DEFINE_TEX2D_SAMPLE(2, _texture);

DEFINE_STORAGE(3, TileData, _instances);

PUSH_CONSTANT Constants constants;

[shader("vertex")]
V2F VertexMain(Vertex input)
{
    V2F output;
    
    TileData tileData = _instances[input.instanceId];
    
    float3 pos = input.position;
    float2 tilePos = tileData.position;

    float3 worldPosition = pos + float3(tilePos, 0.0);
    
    float4 position = mul(constants.model, float4(worldPosition, 1.0));
    
    // Store world position for noise calculation
    output.worldPosition = position;
    
    position = mul(viewProjection, position);
    output.position = position;
    output.uv = input.uv;
    
    return output;
}

[shader("pixel")]
float4 PixelMain(V2F input)
    : SV_TARGET
{
    float4 color = SAMPLE_TEX2D(_texture, input.uv);
    
    // Water ripple effect using noise
    float time = globalRenderData.time * 0.8;

    // Primary noise for water movement
    fnl_state state = fnlCreateState(1337);
    state.noise_type = FNL_NOISE_CELLULAR;
    state.fractal_type = FNL_FRACTAL_FBM;
    state.frequency = 0.8;
    state.octaves = 2;
    state.lacunarity = 2.0f;
    state.gain = 0.5f;

    float noise = (fnlGetNoise3D(state, input.worldPosition.x, time, input.worldPosition.y) + 1.0) * 0.5;

    // Secondary noise for surface detail
    fnl_state state2 = fnlCreateState(1337);
    state2.noise_type = FNL_NOISE_VALUE;
    state2.frequency = 2.0;

    float noise2 = (fnlGetNoise2D(state2, input.worldPosition.x + time, input.worldPosition.y + time) + 1.0) * 0.5;

    // Apply noise to create water ripple effect
    color.rgba += (1.0 - color.a) * noise;
    
    // Add surface shimmer using second noise
    float4 edgeColor = float4(1.0, 1.0, 1.0, 1.0);
    edgeColor.rgb *= noise2 * 2.0;
    
    // Simple blend without complex edge detection
    float shimmerStrength = 0.3; // Adjustable shimmer intensity
    color = lerp(color, edgeColor, shimmerStrength * noise2);

    return color;
} 