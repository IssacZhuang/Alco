// file "clear-texture.slang" - module "clear_texture"
//
// Slang module converted from Shaders/Pipelines/Compute/ClearTexture.hlsl (Phase 2 migration).
// Resources are grouped as ParameterBlocks — the compiler assigns each block
// its own descriptor set in declaration order; members resolve by bare field name.

#language slang 2025
module clear_texture;

import alco_rendering_core;

struct Constants{
    float4 color;
};

public struct TextureParams
{
    public [[vk::image_format("rgba16f")]] RWTexture2D<float4> _texture;
};

public ParameterBlock<TextureParams> _texture;

[shader("compute")]
[numthreads(16, 16, 1)]
void MainCS(uint3 id : SV_DispatchThreadID) {
    _texture._texture[id.xy] = float4(0, 0, 0, 0);
}
