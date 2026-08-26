// file "BlitRegion.slang" - module "BlitRegion"
//
// Resources are grouped as ParameterBlocks — the compiler assigns each block
// its own descriptor set in declaration order; members resolve by bare field name.

#language slang 2025
module BlitRegion;

import AlcoRendering_Core;

// Full-screen blit that samples a centered sub-region of the source texture,
// applying both crop and downscale in a single pass via bilinear sampling.
// `uvScale`/`uvOffset` select the source window; the GPU handles the downscale
// because the source region is larger than the destination quad.
struct Constants {
  float2 uvOffset; // Top-left (min) corner of the sampled source window, in [0,1].
  float2 uvScale;  // Size of the sampled source window, in [0,1] on each axis.
};

public struct PassParams
{
    public Texture2D texture;
};

public ParameterBlock<PassParams> pass;
[[vk::push_constant]] Constants constants;

struct Vertex {
  float3 position : POSITION;
  float2 uv : TEXCOORD0;
};

struct V2F {
  float4 position : SV_POSITION;
  float2 uv : TEXCOORD0;
};

[shader("vertex")]
V2F MainVS(Vertex input) {
  V2F output = (V2F)0;
  output.position = float4(input.position, 1.0f);
  // Map the full-screen quad's [0,1] UVs into the cropped source window.
  output.uv = input.uv * constants.uvScale + constants.uvOffset;
  return output;
}

[shader("fragment")]
float4 MainPS(V2F input) : SV_TARGET {
  return pass.texture.Sample(samplers.linearClamp, input.uv);
}
