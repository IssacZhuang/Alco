// file "tile-connectable.slang" - module "tile_connectable"
//
// Resources are grouped as ParameterBlocks — the compiler assigns
// each block its own descriptor set in declaration order; members resolve by
// bare field name.

#language slang 2025
module tile_connectable;

struct Vertex {
  float3 position : POSITION;
  float2 uv : TEXCOORD0;
};

struct V2F {
  float4 position : SV_POSITION;
  float2 uv : TEXCOORD0;
};

struct Constants {
  float4x4 model;
  float4 color;
  float4 uvRect;
  float2 size;
  float2 offset;
};

public struct CameraParams
{
    public float4x4 viewProjection;
};

public ParameterBlock<CameraParams> camera;

public struct MaterialParams
{
    public Texture2D texture;
    public SamplerState textureSampler;
};

public ParameterBlock<MaterialParams> material;

[[vk::push_constant]] Constants constants;

[shader("vertex")]
V2F MainVS(Vertex input) {
  V2F output = (V2F)0;

  float3 pos = input.position;
  //make it render as a facade
  pos.xy *= constants.size;
  pos.z = 0.5f - pos.y;
  pos.xy += constants.offset;
  output.position = mul(constants.model, float4(pos, 1.0f));
  output.position = mul(camera.viewProjection, output.position);
  output.uv = input.uv * constants.uvRect.zw + constants.uvRect.xy;
  return output;
}

[shader("fragment")]
float4 MainPS(V2F input) : SV_TARGET {
  float4 color = material.texture.Sample(material.textureSampler, input.uv) * constants.color;
  return color;
}
