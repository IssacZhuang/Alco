#language slang 2025
module World3DSsrComposite;

import Core;
import SharedSampler;
import World3DSceneData;
import World3DShadowSample;

[GPURoot]
public struct SsrCompositeRoot
{
    public GPUBufferView Views;
    public GPUBufferView ShadowFrame;
    public GPUTextureId HdrInTex;
    public GPUTextureId ResolvedSsrTex;
    public GPUTextureId AlbedoTex;
    public GPUTextureId NormalTex;
    public GPUTextureId MaterialTex;
    public GPUTextureId DepthTex;
    public GPUTextureId GiSpecularTex;
    public GPUTextureId HdrOutTex;
    public uint TraceWidth;
    public uint TraceHeight;
    public uint HalfResolution;
    public float Intensity;
    public float MaxRoughness;
    public float GiSpecularScale;
    public uint GiSpecularValid;
    public uint DisableVoxelFallback;
};

float3 ssrEnvSpecularBrdfApprox(float3 f0, float roughness, float ndotV)
{
    const float4 c0 = float4(-1.0, -0.0275, -0.572, 0.022);
    const float4 c1 = float4( 1.0,  0.0425,  1.040, -0.040);
    float4 r = roughness * c0 + c1;
    float a004 = min(r.x * r.x, exp2(-9.28 * ndotV)) * r.x + r.y;
    float2 ab = float2(-1.04, 1.04) * a004 + r.zw;
    return f0 * ab.x + ab.y;
}

float4 sampleBilateralSsr(
    Texture2D<float4> ssrTex,
    Texture2D<float4> normalTex,
    Texture2D<float> depthTex,
    World3DView view,
    uint2 traceDims,
    int2 fullDims,
    float2 uv,
    float3 centerNormal,
    float centerDepth)
{
    float centerLinearZ = view.EyeLocalNear.w / max(centerDepth, 1e-6);
    float2 tracePos = uv * float2(traceDims) - float2(0.5);
    int2 baseCoord = int2(floor(tracePos));
    int2 maxTrace = int2(traceDims) - int2(1, 1);
    int2 maxFull = fullDims - int2(1, 1);

    float4 accum = float4(0.0);
    float weightSum = 0.0;
    [unroll]
    for (int dy = 0; dy <= 1; ++dy)
    {
        [unroll]
        for (int dx = 0; dx <= 1; ++dx)
        {
            int2 tc = clamp(baseCoord + int2(dx, dy), int2(0, 0), maxTrace);
            float2 tcUv = (float2(tc) + 0.5) / float2(traceDims);
            int2 fullCoord = clamp(int2(tcUv * float2(fullDims)), int2(0, 0), maxFull);
            float td = depthTex.Load(int3(fullCoord, 0));
            if (td <= 0.0)
                continue;

            float tLinearZ = view.EyeLocalNear.w / max(td, 1e-6);
            float3 tn = normalize(normalTex.Load(int3(fullCoord, 0)).xyz * 2.0 - 1.0);
            float depthW = exp(-abs(tLinearZ - centerLinearZ) / max(centerLinearZ * 0.1, 0.25));
            float normW = pow(saturate(dot(centerNormal, tn)), 4.0);
            float2 delta = float2(tc) - tracePos;
            float spatialW = exp(-dot(delta, delta) * 1.15);
            float w = spatialW * depthW * normW;
            accum += ssrTex.Load(int3(tc, 0)) * w;
            weightSum += w;
        }
    }
    return weightSum > 1e-5
        ? accum / weightSum
        : ssrTex.SampleLevel(shared_sampler_linear_clamp(), uv, 0.0);
}

[shader("compute")]
[numthreads(8, 8, 1)]
public void csMain(uint3 id : SV_DispatchThreadID)
{
    SsrCompositeRoot root = gpu_load_root<SsrCompositeRoot>();
    World3DView view = gpu_load<World3DView>(root.Views, 0u);
    uint2 fullDims = uint2(view.ViewportSizeInvSize.xy);
    if (any(id.xy >= fullDims))
        return;

    Texture2D<float4> hdrIn = gpu_texture<Texture2D<float4>>(root.HdrInTex.index);
    RWTexture2D<float4> hdrOut = gpu_texture<RWTexture2D<float4>>(root.HdrOutTex.index);
    float4 baseHdr = hdrIn.Load(int3(id.xy, 0));

    World3DShadowFrameGpu shadowFrame = gpu_load<World3DShadowFrameGpu>(root.ShadowFrame, 0u);
    Texture2D<float> depthTex = gpu_texture<Texture2D<float>>(root.DepthTex.index);
    float depth = depthTex.Load(int3(id.xy, 0));
    if (depth <= 0.0)
    {
        if (shadowFrame.DebugView == 12u || shadowFrame.DebugView == 13u)
            hdrOut[id.xy] = float4(0.0, 0.0, 0.0, 1.0);
        else
            hdrOut[id.xy] = baseHdr;
        return;
    }

    Texture2D<float4> normalTex = gpu_texture<Texture2D<float4>>(root.NormalTex.index);
    Texture2D<float4> ssrTex = gpu_texture<Texture2D<float4>>(root.ResolvedSsrTex.index);
    float3 n = normalize(normalTex.Load(int3(id.xy, 0)).xyz * 2.0 - 1.0);
    float2 uv = (float2(id.xy) + 0.5) * view.ViewportSizeInvSize.zw;

    float4 ssr = root.HalfResolution != 0u
        ? sampleBilateralSsr(ssrTex, normalTex, depthTex, view, uint2(root.TraceWidth, root.TraceHeight), int2(fullDims), uv, n, depth)
        : ssrTex.Load(int3(id.xy, 0));

    if (shadowFrame.DebugView == 12u)
    {
        hdrOut[id.xy] = float4(ssr.rgb * ssr.a * root.Intensity, 1.0);
        return;
    }
    if (shadowFrame.DebugView == 13u)
    {
        hdrOut[id.xy] = float4(ssr.aaa, 1.0);
        return;
    }

    Texture2D<float4> matTex = gpu_texture<Texture2D<float4>>(root.MaterialTex.index);
    Texture2D<float4> albedoTex = gpu_texture<Texture2D<float4>>(root.AlbedoTex.index);
    float3 mat = matTex.Load(int3(id.xy, 0)).rgb;
    float roughness = clamp(mat.y, 0.02, 1.0);
    float roughFade = 1.0 - smoothstep(root.MaxRoughness * 0.65, root.MaxRoughness, roughness);
    float conf = saturate(ssr.a * roughFade);
    if (conf <= 0.001)
    {
        hdrOut[id.xy] = baseHdr;
        return;
    }

    float3 srgbAlbedo = albedoTex.Load(int3(id.xy, 0)).rgb;
    float3 lo = srgbAlbedo * (1.0 / 12.92);
    float3 hi = pow(max((srgbAlbedo + 0.055) * (1.0 / 1.055), 0.0), float3(2.4));
    float3 albedo = lerp(hi, lo, step(srgbAlbedo, float3(0.04045)));

    float2 ndc = uv * float2(2.0, -2.0) + float2(-1.0, 1.0);
    float4 eyeH = mul(float4(ndc, depth, 1.0), view.CameraInvVP);
    float3 ray = normalize(eyeH.xyz);
    float ndotV = saturate(dot(n, -ray));
    float3 f0 = lerp(float3(0.04), albedo, mat.x);
    float3 envBrdf = ssrEnvSpecularBrdfApprox(f0, roughness, ndotV);

    float3 ssrContrib = ssr.rgb * envBrdf * mat.z * root.Intensity * conf;
    float3 replaceGi = float3(0.0);
    if (root.DisableVoxelFallback == 0u && root.GiSpecularValid != 0u && root.GiSpecularTex.index != 0u)
    {
        float3 giSpec = gpu_texture<Texture2D<float4>>(root.GiSpecularTex.index).Load(int3(id.xy, 0)).rgb;
        replaceGi = giSpec * root.GiSpecularScale * envBrdf * mat.z * conf;
    }

    hdrOut[id.xy] = float4(max(baseHdr.rgb - replaceGi, 0.0) + ssrContrib, 1.0);
}
