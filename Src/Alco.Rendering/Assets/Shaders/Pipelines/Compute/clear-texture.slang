// file "clear-texture.slang" - module "clear_texture"
//
// Slang module converted from Shaders/Pipelines/Compute/ClearTexture.hlsl (Phase 2 migration).
// DEFINE_* macros became explicit set/binding declarations; bindings within a
// set are assigned in the original declaration order and are compiler-owned.

#language slang 2025
module clear_texture;

import alco_rendering_core;

struct Constants{
    float4 color;
};

cbuffer _texture : register(b0, space0)
{
    [[vk::image_format("rgba16f")]] RWTexture2D<float4> _texture;
};

[shader("compute")]
[numthreads(16, 16, 1)]
void MainCS(uint3 id : SV_DispatchThreadID) {
    _texture[id.xy] = float4(0, 0, 0, 0);
}
