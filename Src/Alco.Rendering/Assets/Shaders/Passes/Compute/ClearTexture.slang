// file "ClearTexture.slang" - module "ClearTexture"
//
// Resources are grouped as ParameterBlocks — the compiler assigns each block
// its own descriptor set in declaration order; members resolve by bare field name.

#language slang 2025
module ClearTexture;

import AlcoRendering_Core;

struct Constants{
    float4 color;
};

public struct TextureParams
{
    public [[vk::image_format("rgba16f")]] RWTexture2D<float4> texture;
};

public ParameterBlock<TextureParams> texture;

[shader("compute")]
[numthreads(16, 16, 1)]
void mainCS(uint3 id : SV_DispatchThreadID) {
    texture.texture[id.xy] = float4(0, 0, 0, 0);
}
