#include "Shaders/Libs/Core.hlsli"
#include "Shaders/Libs/Noise.hlsli"
#include "Shaders/Libs/Time.hlsli"

struct Vertex {
  float3 position : POSITION;
  float2 uv : TEXCOORD0;
  uint instanceId : SV_INSTANCEID;
};

struct V2F {
  float4 position : SV_POSITION;
  float4 worldPosition : TEXCOORD0;
  float2 uv : TEXCOORD1;
  uint instanceId : TEXCOORD2;
};

struct Constants{
    float4x4 model;
    int2 size;
};

struct TileData {
    float4 uvRect;
    float4 color;
    float blendPriority;
    float blendFactor;
};

DEFINE_UNIFORM(0, _camera) { float4x4 viewProjection; };

DEFINE_UNIFORM(1, _timeData) { TimeData timeData; };

DEFINE_TEX2D_SAMPLE(2, _texture);

DEFINE_STORAGE(3, TileData, _tileData);

DEFINE_STORAGE(4, uint, _tileIdData);

//the height of the surface, not the water
DEFINE_STORAGE(5, float, _heightData);


PUSH_CONSTANT Constants constants;

float4 SampleTile(uint instanceId, float2 vertexUV, out float blendPriority)
{
    TileData data = _tileData[_tileIdData[instanceId]];
    float2 uv = frac(vertexUV);
    float height = _heightData[instanceId];
    uv = uv * data.uvRect.zw + data.uvRect.xy;
    blendPriority = data.blendPriority;

    float4 color = SAMPLE_TEX2D(_texture, uv) * data.color;
    color.a = -height;
    return color;
}

//used for standard sprite quad mesh
[shader("vertex")]
V2F VertexMain(Vertex input)
{
    uint tileId = _tileIdData[input.instanceId];
    TileData data = _tileData[tileId];

    V2F output;
    float offsetX = (input.instanceId % constants.size.x) - (constants.size.x-1) *0.5f;
    float offsetY = (input.instanceId / constants.size.x) - (constants.size.y-1) *0.5f;

    float3 pos = input.position;

#if defined(IS_CLIFF)
    offsetY += 1;
    pos.z = pos.y - 0.5f;
#endif

    float4 position = float4(pos, 1);
    position.xy += float2(offsetX, -offsetY);
    position = mul(constants.model, position);

    output.worldPosition = position;

    position = mul(viewProjection, position);
    output.position = position;

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
    float heights[9];
    float4 colors[9];
    float priorities[9];

    TileData data = _tileData[_tileIdData[input.instanceId]];
    float blendFactor = data.blendFactor;

    [unroll]
    for (int i = 0; i < 9; i++)
    {
        int neighborIndex = input.instanceId + offsets[i].x + offsets[i].y * constants.size.x;
        uint tileId = _tileIdData[neighborIndex];
        colors[i] = SampleTile(neighborIndex, input.uv, priorities[i]);
        heights[i] = _heightData[neighborIndex];
    }

    float4 finalColor = colors[4]; // Center color
    float centerPriority = priorities[4];
    float centerHeight = heights[4];

    // Pre-calculate reciprocals
    float invBlendFactor = 1.0 / blendFactor;
    float invEdgeSmoothFactor = 1.0 / 0.2f;//todo: make as property

    // Define blend weights for each neighbor
    float2 uv = input.uv;


    float weights[9] = {
        saturate((uv.x + uv.y) * invBlendFactor),            // top-left
        saturate(uv.y * invBlendFactor),                     // top
        saturate(((1 - uv.x) + uv.y) * invBlendFactor),      // top-right
        saturate(uv.x * invBlendFactor),                     // left
        1.0,                                                 // center
        saturate((1 - uv.x) * invBlendFactor),               // right
        saturate((uv.x + (1 - uv.y)) * invBlendFactor),      // bottom-left
        saturate((1 - uv.y) * invBlendFactor),               // bottom
        saturate(((1 - uv.x) + (1 - uv.y)) * invBlendFactor) // bottom-right
    };

    float weightsHeight[9] = {
        saturate((uv.x + uv.y) * invEdgeSmoothFactor),            // top-left
        saturate(uv.y * invEdgeSmoothFactor),                     // top
        saturate(((1 - uv.x) + uv.y) * invEdgeSmoothFactor),      // top-right
        saturate(uv.x * invEdgeSmoothFactor),                     // left
        1.0,                                                      // center
        saturate((1 - uv.x) * invEdgeSmoothFactor),               // right
        saturate((uv.x + (1 - uv.y)) * invEdgeSmoothFactor),      // bottom-left
        saturate((1 - uv.y) * invEdgeSmoothFactor),               // bottom
        saturate(((1 - uv.x) + (1 - uv.y)) * invEdgeSmoothFactor) // bottom-right
    };

    float time = timeData.time*0.8;

    fnl_state state = fnlCreateState(1337);
    state.noise_type = FNL_NOISE_CELLULAR;
    state.fractal_type = FNL_FRACTAL_FBM;
    state.frequency = 0.8;
    state.octaves = 2;
    state.lacunarity = 2.f;
    state.gain = .5f;

    float noise = (fnlGetNoise3D(state, input.worldPosition.x, time, input.worldPosition.y)+1)*0.5;

    float finalDarkening = 1;

    // Apply blending for all neighbors in a single loop
    [unroll]
    for (int j = 0; j < 9; j++)
    {
        if (j != 4) // Skip center tile
        {
            bool isAboveWater = heights[j] >= -0.001f;
            if (isAboveWater) {
                finalDarkening = lerp(0.7, finalDarkening, weightsHeight[j]);
            }
            
            if (priorities[j] > centerPriority||heights[j] < centerHeight)
            {
                finalColor = lerp(colors[j], finalColor, weights[j]);
            }
        }
    }

    fnl_state state2 = fnlCreateState(1337);
    state2.noise_type = FNL_NOISE_VALUE;
    state2.frequency = 2;

    float noise2 = (fnlGetNoise2D(state2, input.worldPosition.x + time, input.worldPosition.y + time)+1)*0.5;

    finalColor.rgba += (1-finalColor.a)*noise;
    // blend with white
    float4 edgeColor = float4(1, 1, 1, 1);
    edgeColor.rgb *= (noise2)*2;
    finalColor = lerp(finalColor, edgeColor, 1-finalDarkening);

    return finalColor;
}

