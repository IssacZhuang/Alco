#include "Shaders/Libs/Core.hlsli"
#include "Shaders/Libs/BCCompress.hlsli"
#include "Shaders/Libs/UtilsCompress.hlsli"

DEFINE_STORAGE(0, uint4, _output);
DEFINE_TEX2D_READ(1, _input);
DEFINE_UNIFORM(2, _data) {
    uint2 size;
};

uint GetOutputIndex(uint2 pos)
{
    return pos.y * size.x + pos.x;
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
