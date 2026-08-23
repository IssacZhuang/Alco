// file "texture-compress-bc3.slang" - module "texture_compress_b_c3"
//
// Slang module converted from Shaders/Pipelines/Compute/Compress/TextureCompressBC3.hlsl (Phase 2 migration).
// DEFINE_* macros became explicit set/binding declarations; bindings within a
// set are assigned in the original declaration order and are compiler-owned.

#language slang 2025
module texture_compress_bc3;

import alco_rendering_core;
import alco_rendering_compression_bc;

struct Constants {
    uint2 size;
};

[[vk::binding(0, 0)]] RWStructuredBuffer<uint4> _output;
[[vk::binding(0, 1)]] Texture2D _input;

[[vk::push_constant]] Constants constants;

uint GetOutputIndex(uint2 pos)
{
    return pos.y * constants.size.x + pos.x;
}

[shader("compute")]
[numthreads(8, 8, 1)]
void MainCS(uint3 id : SV_DispatchThreadID)

{
    uint2 SamplePos = id.xy * 4;

    float3 BlockBaseColor[16];
    float BlockA[16];
    ReadBlockRGBA(_input, SamplePos, BlockBaseColor, BlockA);

    // The sRGB choice is a compile-time define of this module (set by the
    // CPU compressor per texture format); the library exports both paths.
#if defined(IS_SRGB)
    _output[GetOutputIndex(id.xy)] = CompressBC3BlockSrgb(BlockBaseColor, BlockA);
#else
    _output[GetOutputIndex(id.xy)] = CompressBC3Block(BlockBaseColor, BlockA);
#endif
}
