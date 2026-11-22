// file "BloomGaussianV.slang" - module "BloomGaussianV"
//
// Vertical separable gaussian pass using bank-conflict-free SoA groupshared LDS caching,
// stage tint multiplication, and bilinear upsample addition of the coarser accumulation.

#language slang 2025
module BloomGaussianV;

import Core;
import SharedSampler;
import AlcoRendering_Bloom;

[GPURoot]
public struct BloomGaussianVRoot
{
    public GpuTextureId Source;
    public GpuTextureId Additive;
    public GpuTextureId Dest;
    public float2 InvDestSize;
    public uint2 Size;
    public float TintR;
    public float TintG;
    public float TintB;
    public float SigmaRadius;
    public int IntegerRadius;
    public float AdditiveBlend;
};

groupshared float gsR[BLOOM_GAUSSIAN_V_CACHE_H][BLOOM_GAUSSIAN_V_TILE_X];
groupshared float gsG[BLOOM_GAUSSIAN_V_CACHE_H][BLOOM_GAUSSIAN_V_TILE_X];
groupshared float gsB[BLOOM_GAUSSIAN_V_CACHE_H][BLOOM_GAUSSIAN_V_TILE_X];
groupshared float gsWeights[BLOOM_MAX_KERNEL_TAPS];

[shader("compute")]
[numthreads(BLOOM_GAUSSIAN_V_TILE_X, BLOOM_GAUSSIAN_V_TILE_Y, 1)]
void mainCS(uint3 groupId : SV_GroupID, uint3 groupThread : SV_GroupThreadID)
{
    BloomGaussianVRoot root = gpu_load_root<BloomGaussianVRoot>();

    int radius = min(max(root.IntegerRadius, 1), BLOOM_MAX_RADIUS);
    uint tapCount = uint(2 * radius + 1);
    int totalRows = int(BLOOM_GAUSSIAN_V_TILE_Y) + 2 * radius;

    uint tx = groupThread.x;
    uint ty = groupThread.y;
    uint pixelX = groupId.x * BLOOM_GAUSSIAN_V_TILE_X + tx;
    uint pixelY = groupId.y * BLOOM_GAUSSIAN_V_TILE_Y + ty;

    int sampleX = clamp(int(pixelX), 0, max(int(root.Size.x) - 1, 0));
    int tileStartY = int(groupId.y * BLOOM_GAUSSIAN_V_TILE_Y) - radius;
    int maxY = max(int(root.Size.y) - 1, 0);

    Texture2D<float4> source = gpu_texture<Texture2D<float4>>(root.Source.index);
    for (int row = int(ty); row < totalRows; row += int(BLOOM_GAUSSIAN_V_TILE_Y))
    {
        int sampleY = clamp(tileStartY + row, 0, maxY);
        float3 sampleColor = source.Load(int3(sampleX, sampleY, 0)).rgb;
        gsR[row][tx] = sampleColor.r;
        gsG[row][tx] = sampleColor.g;
        gsB[row][tx] = sampleColor.b;
    }

    uint flatId = ty * BLOOM_GAUSSIAN_V_TILE_X + tx;
    if (flatId < tapCount)
    {
        int offset = int(flatId) - radius;
        gsWeights[flatId] = bloomGaussianWeight(float(offset), root.SigmaRadius);
    }

    GroupMemoryBarrierWithGroupSync();

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
        uint cacheY = ty + k;
        sum += float3(gsR[cacheY][tx], gsG[cacheY][tx], gsB[cacheY][tx]) * w;
    }

    float3 color = (sum / weightSum) * float3(root.TintR, root.TintG, root.TintB);
    if (root.AdditiveBlend > 0.5f)
    {
        float2 uv = (float2(pixelX, pixelY) + 0.5f) * root.InvDestSize;
        Texture2D<float4> additive = gpu_texture<Texture2D<float4>>(root.Additive.index);
        color += additive.SampleLevel(shared_sampler_linear_clamp(), uv, 0.0f).rgb;
    }

    RWTexture2D<float4> dest = gpu_texture<RWTexture2D<float4>>(root.Dest.index);
    dest[uint2(pixelX, pixelY)] = float4(color, 1.0f);
}
