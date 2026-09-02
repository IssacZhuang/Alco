// file "TextureCompressBc1.slang" - module "TextureCompressBc1"
//
// Resources are grouped as ParameterBlocks — the compiler assigns each block
// its own descriptor set in declaration order; members resolve by bare field name.

#language slang 2025
module TextureCompressBc1;

import AlcoRendering_Core;
import AlcoRendering_CompressionBC;

struct Constants {
    uint2 size;
};

public struct PassParams
{
    public RWStructuredBuffer<uint2> output;
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
    // The thread group covers 8x8 blocks; a small image dispatches the same
    // group with fewer valid blocks, and the out-of-range threads' output
    // indices would alias back into the valid region — bail out early.
    if (id.x >= constants.size.x || id.y >= constants.size.y)
    {
        return;
    }

    uint2 samplePos = id.xy * 4;

    float3 blockBaseColor[16];
    float blockA[16];
    readBlockRGBA(input.input, samplePos, blockBaseColor, blockA);

    // The sRGB choice is a generic value specialization of this module (the
    // CPU compressor requests mainCS<true> or mainCS<false> per texture format);
    // the library exports both paths. BC1 has no alpha channel: blockA is unused.
    if (isSRGB)
    {
        pass.output[getOutputIndex(id.xy)] = compressBC1BlockSrgb(blockBaseColor);
    }
    else
    {
        pass.output[getOutputIndex(id.xy)] = compressBC1Block(blockBaseColor);
    }
}
