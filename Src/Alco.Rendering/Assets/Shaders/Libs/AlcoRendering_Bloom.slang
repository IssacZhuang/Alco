// file "AlcoRendering_Bloom.slang" - module "AlcoRendering_Bloom"
//
// Shared constants and gaussian weight helper for bindless Compute Bloom passes.

#language slang 2025
module AlcoRendering_Bloom;

public static const int BLOOM_MAX_RADIUS = 31;
public static const int BLOOM_MAX_KERNEL_TAPS = BLOOM_MAX_RADIUS * 2 + 1;
public static const uint BLOOM_SETUP_THREADS = 8;
public static const uint BLOOM_DOWNSAMPLE_THREADS = 8;
public static const uint BLOOM_GAUSSIAN_H_THREADS = 64;
public static const uint BLOOM_GAUSSIAN_H_CACHE_W = BLOOM_GAUSSIAN_H_THREADS + uint(BLOOM_MAX_RADIUS) * 2;
public static const uint BLOOM_GAUSSIAN_V_TILE_X = 8;
public static const uint BLOOM_GAUSSIAN_V_TILE_Y = 32;
public static const uint BLOOM_GAUSSIAN_V_CACHE_H = BLOOM_GAUSSIAN_V_TILE_Y + uint(BLOOM_MAX_RADIUS) * 2;

[ForceInline]
public float bloomGaussianWeight(float x, float sigmaRadius)
{
    float d = x / sigmaRadius;
    return exp(-16.7f * d * d);
}
