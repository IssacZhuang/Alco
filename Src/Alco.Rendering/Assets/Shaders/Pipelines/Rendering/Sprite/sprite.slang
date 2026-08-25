// file "sprite.slang" - module "sprite"
//
// Slang module converted from Shaders/Pipelines/Rendering/Sprite/Sprite.hlsl (Phase 2 migration).
// Resources are grouped as ParameterBlocks — the compiler assigns each block
// its own descriptor set in declaration order; members resolve by bare field name.
// The REPEATED texture-wrap toggle is the module's one generic value axis
// (MainPS<let Repeated : bool>) — requested as a specialization argument where
// the retired define used to be.

#language slang 2025
module sprite;

import alco_rendering_core;

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
};

public struct CameraParams
{
    public float4x4 viewProjection;
};

public ParameterBlock<CameraParams> _camera;


public struct MaterialParamsData
{
    public Texture2D _texture;
    public SamplerState _textureSampler;
};

public ParameterBlock<MaterialParamsData> _material;

[[vk::push_constant]] Constants constants;

[shader("vertex")]
V2F MainVS(Vertex input) {
  V2F output = (V2F)0;
  output.position = mul(constants.model, float4(input.position, 1.0f));
  output.position = mul(_camera.viewProjection, output.position);
  output.uv = input.uv * constants.uvRect.zw + constants.uvRect.xy;
  return output;
}

[shader("fragment")]
float4 MainPS<let Repeated : bool>(V2F input) : SV_TARGET {
  float2 uv = input.uv;
  // Repeated-wrap sampling folds away for the default (false) specialization.
  if (Repeated) { uv = frac(uv); }
  float4 color = _material._texture.Sample(_material._textureSampler, uv) * constants.color;
  return color;
}