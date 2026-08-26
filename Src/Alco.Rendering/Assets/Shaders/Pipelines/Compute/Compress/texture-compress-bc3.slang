// file "texture-compress-bc3.slang" - module "texture_compress_b_c3"
//
// Slang module converted from Shaders/Pipelines/Compute/Compress/TextureCompressBC3.hlsl (Phase 2 migration).
// Resources are grouped as ParameterBlocks — the compiler assigns each block
// its own descriptor set in declaration order; members resolve by bare field name.

#language slang 2025
module texture_compress_bc3;

import alco_rendering_core;
import alco_rendering_compression_bc;

struct Constants {
    uint2 size;
};

public struct PassParams
{
    public RWStructuredBuffer<uint4> output;
};

public ParameterBlock<PassParams> pass;
public struct InputParams
{
    public Texture2D input;
};

public ParameterBlock<InputParams> input;

[[vk::push_constant]] Constants constants;

uint getOutputIndex(uint2 pos)
{
    return pos.y * constants.size.x + pos.x;
}

[shader("compute")]
[numthreads(8, 8, 1)]
void mainCS<let isSRGB : bool>(uint3 id : SV_DispatchThreadID)

{
    uint2 samplePos = id.xy * 4;

    float3 blockBaseColor[16];
    float blockA[16];
    readBlockRGBA(input.input, samplePos, blockBaseColor, blockA);

    // The sRGB choice is a generic value specialization of this module (the
    // CPU compressor requests mainCS<true> or mainCS<false> per texture format);
    // the library exports both paths.
    if (isSRGB)
    {
        pass.output[getOutputIndex(id.xy)] = compressBC3BlockSrgb(blockBaseColor, blockA);
    }
    else
    {
        pass.output[getOutputIndex(id.xy)] = compressBC3Block(blockBaseColor, blockA);
    }
}
