#language slang 2025
module World3DVoxelBlueNoise;

import Core;

[GPURoot]
public struct VoxelBlueNoiseRoot
{
    public GPUTextureId OutputLut;
    public uint Size;
    public uint Seed;
    public uint Reserved0;
};

uint hash32(uint v)
{
    v ^= v >> 16;
    v *= 0x7feb352du;
    v ^= v >> 15;
    v *= 0x846ca68bu;
    v ^= v >> 16;
    return v;
}

float u01(uint v)
{
    return float(v & 0x00FFFFFFu) * (1.0 / 16777216.0);
}

[shader("compute")]
[numthreads(8, 8, 1)]
public void csMain(uint3 id : SV_DispatchThreadID)
{
    VoxelBlueNoiseRoot root = gpu_load_root<VoxelBlueNoiseRoot>();
    if (id.x >= root.Size || id.y >= root.Size)
        return;

    // 2D interleaved R2 low-discrepancy sequence rotated per 8x8 tile for high-frequency spatial distribution.
    float ign = frac(52.9829189 * frac(0.06711056 * float(id.x) + 0.00583715 * float(id.y)));
    uint h0 = hash32(id.x + id.y * root.Size + root.Seed * 747796405u);
    uint h1 = hash32(h0 ^ 0x9e3779b9u);
    uint h2 = hash32(h1 ^ 0x85ebca6bu);
    uint h3 = hash32(h2 ^ 0xc2b2ae35u);

    float4 value = frac(float4(
        ign,
        frac(0.7548776662 * float(id.x) + 0.5698402909 * float(id.y) + 0.25 * u01(h0)),
        frac(0.5698402909 * float(id.x) + 0.7548776662 * float(id.y) + 0.25 * u01(h1)),
        0.5 * ign + 0.5 * u01(h2) + 0.125 * u01(h3)));

    RWTexture2D<float4> lut = gpu_texture<RWTexture2D<float4>>(root.OutputLut.index);
    lut[id.xy] = value;
}
