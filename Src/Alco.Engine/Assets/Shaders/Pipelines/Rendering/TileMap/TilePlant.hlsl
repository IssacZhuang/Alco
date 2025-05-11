#include "Shaders/Libs/Core.hlsli"
#include "Shaders/Libs/GlobalRenderData.hlsli"

struct Vertex {
  float3 position : POSITION;
  float2 uv : TEXCOORD0;
  uint instanceId : SV_INSTANCEID;
};

struct V2F {
  float4 position : SV_POSITION;
  float2 uv : TEXCOORD0;
#if defined(USE_LIGHT_MAP)
  float2 lightMapUV : TEXCOORD1;
  uint instanceId : TEXCOORD2;
#else
  uint instanceId : TEXCOORD1;
#endif
};


struct Constants{
    float4x4 model;
    int2 size;
};

struct TileData{
    float4 uvRect;
    float4 color;
    float2 scale;
    float2 heightOffsetFactor;
    float hasContent;
    float randomOffsetFactor;
};


DEFINE_UNIFORM(0, _camera) { float4x4 viewProjection; };

DEFINE_UNIFORM(1, _globalRenderData) { GlobalRenderData globalRenderData; };

DEFINE_TEX2D_SAMPLE(2, _texture);

DEFINE_STORAGE(3, TileData, _tileData);

DEFINE_STORAGE(4, uint, _tileIdData);

DEFINE_STORAGE(5, float, _heightData);

DEFINE_TEX2D_SAMPLE(6, _lightMap);

PUSH_CONSTANT Constants constants;



[shader("vertex")]
V2F VertexMain(Vertex input)
{
    uint tileId = _tileIdData[input.instanceId];
    TileData data = _tileData[tileId];
    V2F output;
    if (data.hasContent <= 0) {
        return output;
    }
    
    float gridX = input.instanceId % constants.size.x;
    float gridY = input.instanceId / constants.size.x;
    float offsetX = gridX - (constants.size.x-1) *0.5f;
    float offsetY = gridY - (constants.size.y-1) *0.5f;

    float hash = offsetX*17 + offsetY*23;
    float randomOffsetFactor = data.randomOffsetFactor;
    offsetX += sin(hash)*randomOffsetFactor;
    offsetY += cos(hash)*randomOffsetFactor;

    // the vertex position is calculated based on the standard sprite quad mesh
    // which is centered at the origin
    // private static readonly Vertex[] VerticesMidUpSpriteQuad =
    // {
    //     new(new Vector3(-0.5f, 1, 0), new Vector2(0, 0)),
    //     new(new Vector3(0.5f, 1, 0), new Vector2(1, 0)),
    //     new(new Vector3(0.5f, 0, 0), new Vector2(1, 1)),
    //     new(new Vector3(-0.5f, 0, 0), new Vector2(0, 1))
    // };
    float3 pos = input.position * float3(data.scale, 0);
    float waveOffeetX = pos.y * (sin((globalRenderData.time + pos.x + offsetX * offsetY) * 8) * 0.02);
    float waveOffeetY = cos((globalRenderData.time + pos.y + offsetX * offsetY) * 8) * 0.02;
    pos.x += waveOffeetX;
    pos.y *= (1+waveOffeetY);
    pos.z = -pos.y;

    float4 position = float4(pos, 1);

    float height = _heightData[input.instanceId];
    position.z -= height;//z is depth, so it is negative
    position.xy += float2(offsetX, -offsetY) + float2(height, height) * data.heightOffsetFactor;
    position = mul(constants.model, position);
    position = mul(viewProjection, position);
    output.position = position;

    output.uv = input.uv;
    output.instanceId = input.instanceId;

#if defined(USE_LIGHT_MAP)
    output.lightMapUV = float2(gridX + input.position.x, gridY - input.position.y) * data.scale / float2(constants.size.x, constants.size.y) ;
#endif

    return output;
}


[shader("pixel")]

float4 PixelMain(V2F input) : SV_TARGET
{
    uint tileId = _tileIdData[input.instanceId];
    TileData data = _tileData[tileId];
    float2 uv = input.uv;
    uv = uv * data.uvRect.zw + data.uvRect.xy;
    float4 color = SAMPLE_TEX2D(_texture, uv) * data.color;
    if (color.a < 0.01f)
    {
        discard;
    }

#if defined(USE_LIGHT_MAP)
    color.rgb *= SAMPLE_TEX2D(_lightMap, input.lightMapUV).rgb;
#endif
    return color;
}


