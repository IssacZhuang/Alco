#language slang 2025
module World3DSsrColorMip;

import Core;
import SharedSampler;

[GPURoot]
public struct SsrColorMipRoot
{
    public GPUTextureId SrcTex;
    public GPUTextureId DstTex;
    public uint SrcWidth;
    public uint SrcHeight;
    public uint DstWidth;
    public uint DstHeight;
    public uint SrcMip;
    public uint UseKarisWeight;
};

float3 sanitizeSample(float3 c)
{
    return all(isfinite(c)) ? max(c, float3(0.0)) : float3(0.0);
}

float ssrColorLuma(float3 c)
{
    return dot(c, float3(0.2126, 0.7152, 0.0722));
}

[shader("compute")]
[numthreads(8, 8, 1)]
public void csMain(uint3 id : SV_DispatchThreadID)
{
    SsrColorMipRoot root = gpu_load_root<SsrColorMipRoot>();
    if (id.x >= root.DstWidth || id.y >= root.DstHeight)
        return;

    Texture2D<float4> src = gpu_texture<Texture2D<float4>>(root.SrcTex.index);
    RWTexture2D<float4> dst = gpu_texture<RWTexture2D<float4>>(root.DstTex.index);
    SamplerState s = shared_sampler_linear_clamp();

    float2 uv = (float2(id.xy) + 0.5) / float2(max(float(root.DstWidth), 1.0), max(float(root.DstHeight), 1.0));
    float2 invSrc = float2(1.0 / max(float(root.SrcWidth), 1.0), 1.0 / max(float(root.SrcHeight), 1.0));
    float mip = float(root.SrcMip);

    float3 a = sanitizeSample(src.SampleLevel(s, uv + invSrc * float2(-2.0, -2.0), mip).rgb);
    float3 b = sanitizeSample(src.SampleLevel(s, uv + invSrc * float2( 0.0, -2.0), mip).rgb);
    float3 c = sanitizeSample(src.SampleLevel(s, uv + invSrc * float2( 2.0, -2.0), mip).rgb);

    float3 d = sanitizeSample(src.SampleLevel(s, uv + invSrc * float2(-1.0, -1.0), mip).rgb);
    float3 e = sanitizeSample(src.SampleLevel(s, uv + invSrc * float2( 1.0, -1.0), mip).rgb);

    float3 f = sanitizeSample(src.SampleLevel(s, uv + invSrc * float2(-2.0,  0.0), mip).rgb);
    float3 g = sanitizeSample(src.SampleLevel(s, uv,                               mip).rgb);
    float3 h = sanitizeSample(src.SampleLevel(s, uv + invSrc * float2( 2.0,  0.0), mip).rgb);

    float3 i = sanitizeSample(src.SampleLevel(s, uv + invSrc * float2(-1.0,  1.0), mip).rgb);
    float3 j = sanitizeSample(src.SampleLevel(s, uv + invSrc * float2( 1.0,  1.0), mip).rgb);

    float3 k = sanitizeSample(src.SampleLevel(s, uv + invSrc * float2(-2.0,  2.0), mip).rgb);
    float3 l = sanitizeSample(src.SampleLevel(s, uv + invSrc * float2( 0.0,  2.0), mip).rgb);
    float3 m = sanitizeSample(src.SampleLevel(s, uv + invSrc * float2( 2.0,  2.0), mip).rgb);

    float3 result;
    if (root.UseKarisWeight != 0u)
    {
        float3 g0 = (d + e + i + j) * 0.25;
        float3 g1 = (a + b + f + g) * 0.25;
        float3 g2 = (b + c + g + h) * 0.25;
        float3 g3 = (f + g + k + l) * 0.25;
        float3 g4 = (g + h + l + m) * 0.25;

        float w0 = 0.5 / (1.0 + ssrColorLuma(g0));
        float w1 = 0.125 / (1.0 + ssrColorLuma(g1));
        float w2 = 0.125 / (1.0 + ssrColorLuma(g2));
        float w3 = 0.125 / (1.0 + ssrColorLuma(g3));
        float w4 = 0.125 / (1.0 + ssrColorLuma(g4));
        float wSum = max(w0 + w1 + w2 + w3 + w4, 1e-6);

        result = (g0 * w0 + g1 * w1 + g2 * w2 + g3 * w3 + g4 * w4) / wSum;
    }
    else
    {
        result = g * 0.125
            + (a + c + k + m) * 0.03125
            + (b + f + h + l) * 0.0625
            + (d + e + i + j) * 0.125;
    }

    dst[id.xy] = float4(result, 1.0);
}
