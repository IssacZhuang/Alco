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
    public RWStructuredBuffer<uint4> _output;
};

public ParameterBlock<PassParams> _pass;
public struct InputParams
{
    public Texture2D _input;
};

public ParameterBlock<InputParams> _input;

[[vk::push_constant]] Constants constants;

uint GetOutputIndex(uint2 pos)
{
    return pos.y * constants.size.x + pos.x;
}

[shader("compute")]
[numthreads(8, 8, 1)]
void MainCS<let IsSRGB : bool>(uint3 id : SV_DispatchThreadID)

{
    uint2 SamplePos = id.xy * 4;

    float3 BlockBaseColor[16];
    float BlockA[16];
    ReadBlockRGBA(_input._input, SamplePos, BlockBaseColor, BlockA);

    // The sRGB choice is a generic value specialization of this module (the
    // CPU compressor requests MainCS<true> or MainCS<false> per texture format);
    // the library exports both paths.
    if (IsSRGB)
    {
        _pass._output[GetOutputIndex(id.xy)] = CompressBC3BlockSrgb(BlockBaseColor, BlockA);
    }
    else
    {
        _pass._output[GetOutputIndex(id.xy)] = CompressBC3Block(BlockBaseColor, BlockA);
    }
}
