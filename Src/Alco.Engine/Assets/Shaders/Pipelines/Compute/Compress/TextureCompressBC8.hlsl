#include "Shaders/Libs/Core.hlsli"
#include "Shaders/Libs/BCCompress.hlsli"
#include "Shaders/Libs/UtilsCompress.hlsli"

DEFINE_TEX2D_STORAGE(0, _output, uint4, "rgba32ui");
DEFINE_TEX2D_SAMPLE(1, _input);
DEFINE_UNIFORM(2, _data) {
    uint4 DestRect;
};


[shader("compute")]
[numthreads(8, 8, 1)]
void MainCS(uint3 id : SV_DispatchThreadID)
{
    uint2 SamplePos = id.xy * 4;
    if (any(SamplePos >= DestRect.zw))

        return;

    float2 TexelUVSize = 1.f / float2(DestRect.zw);
    float2 SampleUV = (float2(SamplePos)+0.5f) * TexelUVSize;

    float3 BlockBaseColor[16];
    float BlockA[16];
    ReadBlockRGBA(_input, _inputSampler, SampleUV, TexelUVSize, BlockBaseColor, BlockA);


    _output[id.xy] = CompressBC3Block_SRGB(BlockBaseColor, BlockA);
}
