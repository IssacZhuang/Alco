#include "Shaders/Libs/Core.hlsli"
#include "Shaders/Libs/CompressionBC.hlsli"

struct Constants {
    uint2 size;
};

DEFINE_STORAGE(0, uint4, _output);
DEFINE_TEX2D_READ(1, _input);

PUSH_CONSTANT Constants constants;

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

    _output[GetOutputIndex(id.xy)] = CompressBC3Block_SRGB(BlockBaseColor, BlockA);
}
