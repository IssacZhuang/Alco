#include "Shaders/Libs/Core.hlsli"
#include "Shaders/Libs/TextureBombing.hlsli"

struct Vertex
{
    float3 position : POSITION;
    float2 uv : TEXCOORD0;
    uint instanceId : SV_INSTANCEID;
};

struct V2F
{
    float4 position : SV_POSITION;
    float2 uv : TEXCOORD0;
    float2 worldPos : TEXCOORD1;
#if defined(USE_LIGHT_MAP)
    float2 lightMapUV : TEXCOORD2;
    uint instanceId : TEXCOORD3;
#else
    uint instanceId : TEXCOORD2;
#endif
};

struct Constants
{
    float4x4 model;
    int2 size;
};

struct TileData
{
    float4 uvRect;
    float4 color;
    float2 meshScale;
    float2 uvScale;
    float2 heightOffsetFactor;
    float blendPriority;
    float blendFactor;
    float edgeSmoothFactor;
};

DEFINE_UNIFORM(0, _camera)
{
    float4x4 viewProjection;
};

DEFINE_TEX2D_SAMPLE(1, _texture);

DEFINE_STORAGE(2, TileData, _tileData);

DEFINE_STORAGE(3, uint, _tileIdData);

DEFINE_STORAGE(4, float, _heightData);

DEFINE_TEX2D_SAMPLE(5, _lightMap);

PUSH_CONSTANT Constants constants;

float4 SampleTile(uint tileId, float2 vertexUV, float2 worldPos, out float blendPriority)
{
    TileData data = _tileData[tileId];

    // Calculate traditional UV for uvRect sampling
    float2 uv = frac(vertexUV * data.uvScale);
    uv = uv * data.uvRect.zw + data.uvRect.xy;

    blendPriority = data.blendPriority;

#if defined(TEXTURE_BOMBING)
    // Use world position for texture bombing UV
    float2 bombingUV = worldPos * 0.1; // Scale factor for texture bombing

    // Apply texture bombing using world coordinates
    float4 bombedColor = TextureBombing(_texture, _textureSampler, bombingUV, float2(1, 1), data.uvRect.xy);

    // Blend between regular sampling and texture bombing
    float4 regularColor = SAMPLE_TEX2D(_texture, uv);
    float4 finalColor = lerp(regularColor, bombedColor, 0.7); // 70% texture bombing

    return finalColor * data.color;
#else
    // Regular texture sampling without bombing
    return SAMPLE_TEX2D(_texture, uv) * data.color;
#endif
}

[shader("vertex")]
V2F VertexMain(Vertex input)
{
    uint tileId = _tileIdData[input.instanceId];
    TileData data = _tileData[tileId];

    V2F output;
    float gridX = input.instanceId % constants.size.x;
    float gridY = input.instanceId / constants.size.x;
    float offsetX = gridX - (constants.size.x - 1) * 0.5f;
    float offsetY = gridY - (constants.size.y - 1) * 0.5f;


    // the vertex position is calculated based on the standard sprite quad mesh
    // which is centered at the origin
    // private static readonly Vertex[] VerticesSpriteQuad =
    //     {
    //         new(new Vector3(-0.5f, 0.5f, 0), new Vector2(0, 0)),
    //         new(new Vector3(0.5f, 0.5f, 0), new Vector2(1, 0)),
    //         new(new Vector3(0.5f, -0.5f, 0), new Vector2(1, 1)),
    //         new(new Vector3(-0.5f, -0.5f, 0), new Vector2(0, 1))
    //     };
    float3 pos = input.position * float3(data.meshScale, 0);

#if defined(IS_CLIFF)
    //make it render as a facade
    offsetY += 1;
    gridY += 1;
    pos.z = 0.5f - pos.y;
#endif

    float4 position = float4(pos, 1);
    float height = _heightData[input.instanceId];
    position.z -= height;
    position.xy += float2(offsetX, -offsetY) + float2(height, height) * data.heightOffsetFactor;
    position = mul(constants.model, position);
    position = mul(viewProjection, position);
    output.position = position;

    output.uv = input.uv;
    output.instanceId = input.instanceId;

    // Calculate world position for texture bombing (before view-projection transform)
    float4 worldPosition = mul(constants.model, float4(pos + float3(offsetX, -offsetY, -height), 1));
    output.worldPos = worldPosition.xy;

#if defined(USE_LIGHT_MAP)
    output.lightMapUV = float2(gridX + input.position.x, gridY - input.position.y) * data.meshScale / float2(constants.size.x, constants.size.y) ;
#endif


    return output;

}

[shader("pixel")]
float4 PixelMain(V2F input)

    : SV_TARGET
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
    float heights[9];

    TileData data = _tileData[_tileIdData[input.instanceId]];

    float edgeSmoothFactor = data.edgeSmoothFactor;
    float blendFactor = data.blendFactor;

    [unroll]
    for (int i = 0; i < 9; i++)
    {
        int neighborIndex = input.instanceId + offsets[i].x + offsets[i].y * constants.size.x;
        uint tileId = _tileIdData[neighborIndex];
        colors[i] = SampleTile(tileId, input.uv, input.worldPos, priorities[i]);
        heights[i] = _heightData[neighborIndex];
    }

    float4 finalColor = colors[4]; // Center color
    float centerPriority = priorities[4];
    float centerHeight = heights[4];

    // Pre-calculate reciprocals
    float invBlendFactor = 1.0 / blendFactor;
    float invEdgeSmoothFactor = 1.0 / edgeSmoothFactor;

    // Define blend weights for each neighbor
    float2 uv = input.uv;

#if defined(IS_CLIFF)
    float weights[9] = {
        1.0,                                   // top-left
        1.0,                                   // top
        1.0,                                   // top-right
        saturate(uv.x * invBlendFactor),       // left
        1.0,                                   // center
        saturate((1 - uv.x) * invBlendFactor), // right
        1.0,                                   // bottom-left
        1.0,                                   // bottom
        1.0                                    // bottom-right
    };

    float weightsHeight[9] = {
        1.0,                                        // top-left
        1.0,                                        // top
        1.0,                                        // top-right
        saturate(uv.x * invEdgeSmoothFactor),       // left
        1.0,                                        // center
        saturate((1 - uv.x) * invEdgeSmoothFactor), // right
        1.0,                                        // bottom-left
        1.0,                                        // bottom
        1.0                                         // bottom-right
    };
#else
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
#endif

    float finalDarkening = 1;

    // Apply blending for all neighbors in a single loop
    [unroll]
    for (int j = 0; j < 9; j++)
    {
        if (j != 4) // Skip center tile
        {
            float heightDiff = abs(heights[j] - centerHeight);

            if (heightDiff > 0.001f)
            {
                finalDarkening = lerp(0.9, finalDarkening, weightsHeight[j]);
            }
            else if (priorities[j] > centerPriority)
            {
                finalColor = lerp(colors[j], finalColor, weights[j]);
            }
        }
    }

    finalColor *= finalDarkening;

#if defined(USE_LIGHT_MAP)
    //finalColor *= SAMPLE_TEX2D(_lightMap, input.lightMapUV);
    finalColor.rgb *= SAMPLE_TEX2D(_lightMap, input.lightMapUV).rgb;
#endif

    return finalColor;

}

