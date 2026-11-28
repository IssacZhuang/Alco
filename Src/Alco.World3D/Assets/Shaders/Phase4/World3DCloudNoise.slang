#language slang 2025
module World3DCloudNoise;

import Core;
import World3DCloudCommon;

[GPURoot]
public struct CloudNoiseRoot
{
    public GPUTextureId OutputTex;
    public uint VolumeSize;
    public uint Mode;        // 0 = Base 128^3 Perlin-Worley, 1 = Detail 32^3 Worley
    public uint BaseFreq;
    public uint Reserved0;
};

float3 hash33Periodic(float3 p, float period)
{
    float3 q = p - floor(p / period) * period;
    float3 r = frac(q * float3(0.1031, 0.1030, 0.0973));
    r += dot(r, r.yxz + 33.33);
    return frac((r.xxy + r.yxx) * r.zyx);
}

float perlinNoise3D(float3 uvw, float freq)
{
    float3 p = uvw * freq;
    float3 i = floor(p);
    float3 f = p - i;
    float3 u = f * f * f * (f * (f * 6.0 - 15.0) + 10.0);

    float n000 = dot(normalize(hash33Periodic(i + float3(0, 0, 0), freq) * 2.0 - 1.0), f - float3(0, 0, 0));
    float n100 = dot(normalize(hash33Periodic(i + float3(1, 0, 0), freq) * 2.0 - 1.0), f - float3(1, 0, 0));
    float n010 = dot(normalize(hash33Periodic(i + float3(0, 1, 0), freq) * 2.0 - 1.0), f - float3(0, 1, 0));
    float n110 = dot(normalize(hash33Periodic(i + float3(1, 1, 0), freq) * 2.0 - 1.0), f - float3(1, 1, 0));
    float n001 = dot(normalize(hash33Periodic(i + float3(0, 0, 1), freq) * 2.0 - 1.0), f - float3(0, 0, 1));
    float n101 = dot(normalize(hash33Periodic(i + float3(1, 0, 1), freq) * 2.0 - 1.0), f - float3(1, 0, 1));
    float n011 = dot(normalize(hash33Periodic(i + float3(0, 1, 1), freq) * 2.0 - 1.0), f - float3(0, 1, 1));
    float n111 = dot(normalize(hash33Periodic(i + float3(1, 1, 1), freq) * 2.0 - 1.0), f - float3(1, 1, 1));

    float nx00 = lerp(n000, n100, u.x);
    float nx10 = lerp(n010, n110, u.x);
    float nx01 = lerp(n001, n101, u.x);
    float nx11 = lerp(n011, n111, u.x);
    float nxy0 = lerp(nx00, nx10, u.y);
    float nxy1 = lerp(nx01, nx11, u.y);
    return lerp(nxy0, nxy1, u.z) * 0.5 + 0.5;
}

float worleyNoise3D(float3 uvw, float freq)
{
    float3 p = uvw * freq;
    float3 cell = floor(p);
    float3 fracPos = p - cell;

    float minDistSq = 1.0e9;
    [unroll]
    for (int dz = -1; dz <= 1; ++dz)
    {
        [unroll]
        for (int dy = -1; dy <= 1; ++dy)
        {
            [unroll]
            for (int dx = -1; dx <= 1; ++dx)
            {
                float3 offset = float3(float(dx), float(dy), float(dz));
                float3 point = hash33Periodic(cell + offset, freq);
                float3 d = offset + point - fracPos;
                minDistSq = min(minDistSq, dot(d, d));
            }
        }
    }
    return 1.0 - saturate(sqrt(minDistSq));
}

float perlinFbm(float3 uvw, float freq)
{
    float amp = 0.5;
    float sum = 0.0;
    float norm = 0.0;
    float f = freq;
    [unroll]
    for (int i = 0; i < 4; ++i)
    {
        sum += perlinNoise3D(uvw, f) * amp;
        norm += amp;
        f *= 2.0;
        amp *= 0.5;
    }
    return sum / norm;
}

float worleyFbm(float3 uvw, float freq)
{
    float w0 = worleyNoise3D(uvw, freq);
    float w1 = worleyNoise3D(uvw, freq * 2.0);
    float w2 = worleyNoise3D(uvw, freq * 4.0);
    return w0 * 0.625 + w1 * 0.25 + w2 * 0.125;
}

[shader("compute")]
[numthreads(8, 8, 8)]
public void csMain(uint3 id : SV_DispatchThreadID)
{
    CloudNoiseRoot root = gpu_load_root<CloudNoiseRoot>();
    if (any(id >= root.VolumeSize.xxx))
        return;

    float3 uvw = (float3(id) + 0.5) / float(root.VolumeSize);
    RWTexture3D<float4> outTex = gpu_texture<RWTexture3D<float4>>(root.OutputTex.index);

    if (root.Mode == 0u)
    {
        float f = float(max(root.BaseFreq, 2u));
        float perlin = perlinFbm(uvw, f);
        float w0 = worleyFbm(uvw, f);
        float w1 = worleyFbm(uvw, f * 2.0);
        float w2 = worleyFbm(uvw, f * 4.0);
        float pw = cloud_remap(perlin, -(1.0 - w0) * 0.65, 1.0, 0.0, 1.0);
        outTex[id] = float4(saturate(pw), saturate(w0), saturate(w1), saturate(w2));
    }
    else
    {
        float f = float(max(root.BaseFreq, 2u));
        float d0 = worleyFbm(uvw, f);
        float d1 = worleyFbm(uvw, f * 2.0);
        float d2 = worleyFbm(uvw, f * 4.0);
        outTex[id] = float4(saturate(d0), saturate(d1), saturate(d2), 1.0);
    }
}
