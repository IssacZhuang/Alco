#include "Shaders/Libs/Core.hlsli"

struct Vertex {
  float3 position : POSITION;
  float2 uv : TEXCOORD0;
  uint instanceId : SV_INSTANCEID;
};

struct V2F {
  float4 position : SV_POSITION;
  float2 uv : TEXCOORD0;
  uint instanceId : TEXCOORD1;
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
};


DEFINE_UNIFORM(0, _camera) { float4x4 viewProjection; };

DEFINE_TEX2D_SAMPLE(1, _texture);

DEFINE_STORAGE(2, TileData, _tileData);

DEFINE_STORAGE(3, uint, _tileIdData);

DEFINE_STORAGE(4, float, _heightData);

PUSH_CONSTANT Constants constants;



[shader("vertex")]
V2F VertexMain(Vertex input)
{
    uint tileId = _tileIdData[input.instanceId];
    TileData data = _tileData[tileId];

    V2F output;
    float offsetX = (input.instanceId % constants.size.x) - (constants.size.x-1) *0.5f;
    float offsetY = (input.instanceId / constants.size.x) - (constants.size.y-1) *0.5f;

    // the vertex position is calculated based on the standard sprite quad mesh
    // which is centered at the origin
    // private static readonly Vertex[] VerticesSpriteQuad =
    //     {
    //         new(new Vector3(-0.5f, 0.5f, 0), new Vector2(0, 0)),
    //         new(new Vector3(0.5f, 0.5f, 0), new Vector2(1, 0)),
    //         new(new Vector3(0.5f, -0.5f, 0), new Vector2(1, 1)),
    //         new(new Vector3(-0.5f, -0.5f, 0), new Vector2(0, 1))
    //     };
    float3 pos = input.position * float3(data.scale, 0);
    pos.z = pos.y + 0.5f;

    float4 position = float4(pos, 1);
    float height = _heightData[input.instanceId];
    position.z += height;
    position.xy += float2(offsetX, -offsetY) + float2(height, height) * data.heightOffsetFactor;
    position = mul(constants.model, position);
    position = mul(viewProjection, position);
    output.position = position;

    output.uv = input.uv;
    output.instanceId = input.instanceId;

    return output;
}


[shader("pixel")]
float4 PixelMain(V2F input) : SV_TARGET
{
    uint tileId = _tileIdData[input.instanceId];
    TileData data = _tileData[tileId];
    float2 uv = input.uv;
    uv = uv * data.uvRect.zw + data.uvRect.xy;
    return SAMPLE_TEX2D(_texture, uv) * data.color;
}


