// file "sprite-instanced.slang" - module "sprite_instanced"
//
// Slang module converted from Shaders/Pipelines/Rendering/Sprite/SpriteInstanced.hlsl (Phase 2 migration).
// Resources are grouped as ParameterBlocks — the compiler assigns each block
// its own descriptor set in declaration order; members resolve by bare field name.

#language slang 2025
module sprite_instanced;

import alco_rendering_core;

struct Vertex {
  float3 position : POSITION;
  float2 uv : TEXCOORD0;
  uint instanceID : SV_VulkanInstanceID;
};

struct V2F {
  float4 position : SV_POSITION;
  float2 uv : TEXCOORD0;
  uint instanceID : TEXCOORD01;
};

public struct Constants {
  float4x4 model;
  float4 color;
  float4 uvRect;
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
    public RWStructuredBuffer<Constants> instances;
};

public ParameterBlock<InstancesParams> instances;

[shader("vertex")]
V2F MainVS(Vertex input) {
    V2F output = (V2F)0;
  Constants constants = instances.instances[input.instanceID];
  output.position = mul(constants.model, float4(input.position, 1.0f));
  output.position = mul(camera.viewProjection, output.position);
  output.uv = input.uv * constants.uvRect.zw + constants.uvRect.xy;
  output.instanceID = input.instanceID;
  return output;
}

[shader("fragment")]
float4 MainPS(V2F input) : SV_TARGET {
  Constants constants = instances.instances[input.instanceID];
  float4 color = material.texture.Sample(samplers.linearRepeat, input.uv) * constants.color;
  return color;
}