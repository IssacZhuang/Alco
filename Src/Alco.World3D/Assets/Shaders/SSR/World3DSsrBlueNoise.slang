#language slang 2025
module World3DSsrBlueNoise;

import Core;

[GPURoot]
public struct SsrBlueNoiseRoot
{
    public GPUTextureId OutputTex;
    public uint Size;
    public uint Reserved0;
    public uint Reserved1;
    public uint Reserved2;
};

uint bnHash(uint x)
{
    x ^= x >> 16;
    x *= 0x7feb352du;
    x ^= x >> 15;
    x *= 0x846ca68bu;
    x ^= x >> 16;
    return x;
}

float bnToFloat(uint h)
{
    return float(h & 0x00FFFFFFu) * (1.0 / 16777216.0);
}

[shader("compute")]
[numthreads(8, 8, 1)]
public void csMain(uint3 id : SV_DispatchThreadID)
{
    SsrBlueNoiseRoot root = gpu_load_root<SsrBlueNoiseRoot>();
    if (id.x >= root.Size || id.y >= root.Size)
        return;

    // Interleaved 4x4 stratified rank + R2 quasi-random spatial scrambling for smooth half-res temporal convergence.
    static const float bayer16[16] = {
         0.0 / 16.0,  8.0 / 16.0,  2.0 / 16.0, 10.0 / 16.0,
        12.0 / 16.0,  4.0 / 16.0, 14.0 / 16.0,  6.0 / 16.0,
         3.0 / 16.0, 11.0 / 16.0,  1.0 / 16.0,  9.0 / 16.0,
        15.0 / 16.0,  7.0 / 16.0, 13.0 / 16.0,  5.0 / 16.0
    };
    uint rankIdx = ((id.y & 3u) * 4u) + (id.x & 3u);
    float rank = bayer16[rankIdx];

    uint idx = id.y * root.Size + id.x;
    uint h0 = bnHash(idx * 3u + 17u);
    uint h1 = bnHash(idx * 5u + 97u);

    float j0 = frac(rank + bnToFloat(h0) * (1.0 / 16.0));
    float j1 = frac(float(id.x) * 0.7548776662 + float(id.y) * 0.5698402910 + bnToFloat(h1) * 0.25);
    float angle = j1 * (2.0 * 3.14159265359);

    RWTexture2D<float4> outTex = gpu_texture<RWTexture2D<float4>>(root.OutputTex.index);
    outTex[id.xy] = float4(j0, j1, cos(angle), sin(angle));
}
