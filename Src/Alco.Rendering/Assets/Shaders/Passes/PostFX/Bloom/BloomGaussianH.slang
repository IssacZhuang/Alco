// file "BloomGaussianH.slang" - module "BloomGaussianH"
//
// Horizontal separable gaussian pass using groupshared LDS caching (1D tile + halo).
// Mathematically matches the original [-R, R] exp(-16.7 * (x / sigma)^2) kernel.

#language slang 2025
module BloomGaussianH;

import Core;
import AlcoRendering_Bloom;

[GPURoot]
public struct BloomGaussianHRoot
{
    public GPUTextureId Source;
    public GPUTextureId Dest;
    public uint2 Size;
    public float SigmaRadius;
    public int IntegerRadius;
};

groupshared float gsR[BLOOM_GAUSSIAN_H_CACHE_W];
groupshared float gsG[BLOOM_GAUSSIAN_H_CACHE_W];
groupshared float gsB[BLOOM_GAUSSIAN_H_CACHE_W];
groupshared float gsWeights[BLOOM_MAX_KERNEL_TAPS];

[shader("compute")]
[numthreads(BLOOM_GAUSSIAN_H_THREADS, 1, 1)]
void mainCS(uint3 groupId : SV_GroupID, uint3 groupThread : SV_GroupThreadID)
{
    BloomGaussianHRoot root = gpu_load_root<BloomGaussianHRoot>();

    int radius = min(max(root.IntegerRadius, 1), BLOOM_MAX_RADIUS);
    uint tapCount = uint(2 * radius + 1);
    int totalSamples = int(BLOOM_GAUSSIAN_H_THREADS) + 2 * radius;
    int tileStartX = int(groupId.x * BLOOM_GAUSSIAN_H_THREADS) - radius;
    int maxX = max(int(root.Size.x) - 1, 0);
    int clampedY = clamp(int(groupId.y), 0, max(int(root.Size.y) - 1, 0));

    Texture2D<float4> source = gpu_texture<Texture2D<float4>>(root.Source.index);
    for (uint i = groupThread.x; i < uint(totalSamples); i += BLOOM_GAUSSIAN_H_THREADS)
    {
        int sampleX = clamp(tileStartX + int(i), 0, maxX);
        float3 sampleColor = source.Load(int3(sampleX, clampedY, 0)).rgb;
        gsR[i] = sampleColor.r;
        gsG[i] = sampleColor.g;
        gsB[i] = sampleColor.b;
    }

    if (groupThread.x < tapCount)
    {
        int offset = int(groupThread.x) - radius;
        gsWeights[groupThread.x] = bloomGaussianWeight(float(offset), root.SigmaRadius);
    }

    GroupMemoryBarrierWithGroupSync();

    uint pixelX = groupId.x * BLOOM_GAUSSIAN_H_THREADS + groupThread.x;
    uint pixelY = groupId.y;
    if (pixelX >= root.Size.x || pixelY >= root.Size.y)
    {
        return;
    }

    float3 sum = float3(0.0f, 0.0f, 0.0f);
    float weightSum = 0.0f;
    for (uint k = 0; k < tapCount; ++k)
    {
        float w = gsWeights[k];
        weightSum += w;
        uint cacheIdx = groupThread.x + k;
        sum += float3(gsR[cacheIdx], gsG[cacheIdx], gsB[cacheIdx]) * w;
    }

    RWTexture2D<float4> dest = gpu_texture<RWTexture2D<float4>>(root.Dest.index);
    dest[uint2(pixelX, pixelY)] = float4(sum / weightSum, 1.0f);
}
