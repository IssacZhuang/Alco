#include "Shaders/Libs/BCCompress.hlsli"
#include "Shaders/Libs/UtilsCompress.hlsli"


RWTexture2D<uint4> Result;
Texture2D<float4> RenderTexture0;
SamplerState samplerRenderTexture0;
uint4 DestRect;

[shader("compute")]
[numthreads(8, 8, 1)]
void CSMainBC3_8(uint3 ThreadId : SV_DispatchThreadID)
{
    uint2 SamplePos = ThreadId.xy * 4;
    if (any(SamplePos >= DestRect.zw))
        return;

    float2 TexelUVSize = 1.f / float2(DestRect.zw);
    float2 SampleUV = (float2(SamplePos)+0.5f) * TexelUVSize;

    float3 BlockBaseColor[16];
    float BlockA[16];
    ReadBlockRGBA(RenderTexture0, samplerRenderTexture0, SampleUV, TexelUVSize, BlockBaseColor, BlockA);

    Result[ThreadId.xy] = CompressBC3Block_SRGB(BlockBaseColor, BlockA);
}
