#include "Shaders/Libs/Core.hlsli"
#include "Shaders/Libs/Noise.hlsli"

struct Vertex {
  float3 position : POSITION;
  float2 uv : TEXCOORD0;
  uint instanceId : SV_INSTANCEID;
};

struct V2F {
  float4 position : SV_POSITION;
  float4 worldPosition : TEXCOORD0;
  float4 color : COLOR;
  float2 uv : TEXCOORD1;
  uint instanceId : TEXCOORD2;
};

struct Constants{
    float4x4 model;
    int2 size;
};

struct TileData {
    float4 uvRect;
    float2 meshScale;
    float2 uvScale;
    float2 heightOffsetFactor;
    float blendPriority;
    float blendFactor;
    float edgeSmoothFactor;
};

DEFINE_UNIFORM(0, _camera) { float4x4 viewProjection; };

DEFINE_TEX2D_SAMPLE(1, _texture);

DEFINE_STORAGE(2, TileData, _tileData);

DEFINE_STORAGE(3, float4, _colorData);

DEFINE_STORAGE(4, uint, _tileIdData);


PUSH_CONSTANT Constants constants;

float4 SampleTile(uint tileId, float2 vertexUV, out float blendPriority)
{
    TileData data = _tileData[tileId];
    float2 uv = frac(vertexUV * data.uvScale);
    uv = uv * data.uvRect.zw + data.uvRect.xy;
    blendPriority = data.blendPriority;
    return SAMPLE_TEX2D(_texture, uv);
}

//used for standard sprite quad mesh
[shader("vertex")]
V2F VertexMain(Vertex input)
{
    float4 color = _colorData[input.instanceId];
    uint tileId = _tileIdData[input.instanceId];
    TileData data = _tileData[tileId];

    V2F output;
    float offsetX = (input.instanceId % constants.size.x) - (constants.size.x-1) *0.5f;
    float offsetY = (input.instanceId / constants.size.x) - (constants.size.y-1) *0.5f;

    float3 pos = input.position * float3(data.meshScale, 0);

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

    TileData data = _tileData[input.instanceId];
    float blendFactor = data.blendFactor;
    float edgeSmoothFactor = data.edgeSmoothFactor;

    [unroll]
    for (int i = 0; i < 9; i++)
    {
        int neighborIndex = input.instanceId + offsets[i].x + offsets[i].y * constants.size.x;
        uint tileId = _tileIdData[neighborIndex];
        colors[i] = SampleTile(tileId, input.uv, priorities[i]) * input.color;
    }

    float4 finalColor = colors[4]; // Center color
    float centerPriority = priorities[4];

    // Pre-calculate reciprocals
    float invBlendFactor = 1.0 / blendFactor;
    float invEdgeSmoothFactor = 1.0 / edgeSmoothFactor;

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

    fnl_state state = fnlCreateState(1337);
    state.noise_type = FNL_NOISE_CELLULAR;
    state.fractal_type = FNL_FRACTAL_FBM;
    state.frequency = 0.8;
    state.octaves = 2;
    state.lacunarity = 2.f;
    state.gain = .5f;

    float noise = fnlGetNoise2D(state, input.worldPosition.x, input.worldPosition.y);

    

    // Apply blending for all neighbors in a single loop
    [unroll]
    for (int j = 0; j < 9; j++)
    {
        if (j != 4) // Skip center tile
        {
            if (priorities[j] > centerPriority)
            {
                finalColor = lerp(colors[j], finalColor, weights[j]);
            }
        }
    }

    finalColor.rgb += (1+noise)*0.4;

    return finalColor;
}

