#language slang 2025
module World3DSsrTrace;

import Core;
import SharedSampler;
import World3DSceneData;
import World3DAtmosphere;

[GPURoot]
public struct SsrTraceRoot
{
    public float4 SsrParams;            // x = maxDistance, y = thickness, z = maxRoughness, w = edgeFade
    public float4 ScreenAndStepParams;  // x = traceWidth, y = traceHeight, z = hizMipCount, w = maxSteps
    public GPUBufferView Views;
    public GPUBufferView ShadowFrame;
    public GPUTextureId HdrInTex;
    public GPUTextureId NormalTex;
    public GPUTextureId MaterialTex;
    public GPUTextureId DepthTex;
    public GPUTextureId HiZTex;
    public GPUTextureId BlueNoiseTex;
    public GPUTextureId OutRawColorTex;
    public GPUTextureId OutMetaTex;
    public uint FrameIndex;
    public uint HiZActive;
    public uint RejectBackface;
    public uint Reserved0;
};

float ssrLinearViewZ(World3DView view, float depth)
{
    return view.EyeLocalNear.w / max(depth, 1e-6);
}

float3 ssrSampleGGX(float2 xi, float roughness, float3 n)
{
    float a = max(roughness * roughness, 0.004);
    float phi = 2.0 * WORLD3D_PI * xi.x;
    float cosTheta = sqrt(saturate((1.0 - xi.y) / (1.0 + (a * a - 1.0) * xi.y)));
    float sinTheta = sqrt(saturate(1.0 - cosTheta * cosTheta));

    float3 hLocal = float3(cos(phi) * sinTheta, sin(phi) * sinTheta, cosTheta);
    float3 up = abs(n.z) < 0.999 ? float3(0.0, 0.0, 1.0) : float3(1.0, 0.0, 0.0);
    float3 tangent = normalize(cross(up, n));
    float3 bitangent = cross(n, tangent);
    return normalize(tangent * hLocal.x + bitangent * hLocal.y + n * hLocal.z);
}

float2 ssrProjectToUvZ(row_major float4x4 vp, float3 eyeRelPos, out float clipW, out float revZ)
{
    float4 clip = mul(float4(eyeRelPos, 1.0), vp);
    clipW = clip.w;
    if (clip.w <= 1e-5)
    {
        revZ = 1.0;
        return float2(-1.0, -1.0);
    }
    float invW = rcp(clip.w);
    revZ = saturate(clip.z * invW);
    return (clip.xy * invW) * float2(0.5, -0.5) + float2(0.5, 0.5);
}

[shader("compute")]
[numthreads(8, 8, 1)]
public void csMain(uint3 id : SV_DispatchThreadID)
{
    SsrTraceRoot root = gpu_load_root<SsrTraceRoot>();
    uint traceW = uint(root.ScreenAndStepParams.x);
    uint traceH = uint(root.ScreenAndStepParams.y);
    if (id.x >= traceW || id.y >= traceH)
        return;

    RWTexture2D<float4> outColor = gpu_texture<RWTexture2D<float4>>(root.OutRawColorTex.index);
    RWTexture2D<float4> outMeta = gpu_texture<RWTexture2D<float4>>(root.OutMetaTex.index);

    World3DView view = gpu_load<World3DView>(root.Views, 0u);
    uint2 fullDims = max(uint2(view.ViewportSizeInvSize.xy), uint2(1u));
    int2 maxFull = int2(fullDims) - int2(1, 1);

    float2 uv = (float2(id.xy) + 0.5) / float2(float(traceW), float(traceH));
    int2 fullCoord = clamp(int2(uv * float2(fullDims)), int2(0, 0), maxFull);

    Texture2D<float> depthTex = gpu_texture<Texture2D<float>>(root.DepthTex.index);
    float depth = depthTex.Load(int3(fullCoord, 0));
    if (depth <= 0.0)
    {
        outColor[id.xy] = float4(0.0);
        outMeta[id.xy] = float4(0.0);
        return;
    }

    Texture2D<float4> matTex = gpu_texture<Texture2D<float4>>(root.MaterialTex.index);
    float3 mat = matTex.Load(int3(fullCoord, 0)).rgb;
    float roughness = clamp(mat.y, 0.02, 1.0);
    float maxRoughness = root.SsrParams.z;
    if (roughness > maxRoughness)
    {
        outColor[id.xy] = float4(0.0);
        outMeta[id.xy] = float4(uv, 0.0, roughness);
        return;
    }

    Texture2D<float4> normalTex = gpu_texture<Texture2D<float4>>(root.NormalTex.index);
    float3 n = normalize(normalTex.Load(int3(fullCoord, 0)).xyz * 2.0 - 1.0);

    float2 ndc = uv * float2(2.0, -2.0) + float2(-1.0, 1.0);
    float4 eyeH = mul(float4(ndc, depth, 1.0), view.CameraInvVP);
    float3 eyeRelPos = eyeH.xyz / max(eyeH.w, 1e-6);
    float3 v = normalize(-eyeRelPos);

    Texture2D<float4> blueNoiseTex = gpu_texture<Texture2D<float4>>(root.BlueNoiseTex.index);
    uint2 bnCoord = (id.xy + uint2((root.FrameIndex * 19u) & 127u, (root.FrameIndex * 37u) & 127u)) & 127u;
    float4 bn = blueNoiseTex.Load(int3(bnCoord, 0));

    float3 reflDir;
    if (roughness < 0.05)
    {
        reflDir = reflect(-v, n);
    }
    else
    {
        float3 h = ssrSampleGGX(bn.xy, roughness * 0.75, n);
        reflDir = reflect(-v, h);
    }

    if (dot(reflDir, n) <= 0.0)
    {
        outColor[id.xy] = float4(0.0);
        outMeta[id.xy] = float4(uv, 0.0, roughness);
        return;
    }

    float maxDist = max(root.SsrParams.x, 1.0);
    float nearPlane = max(view.EyeLocalNear.w, 0.05);
    float3 rayOrigin = eyeRelPos + n * 0.025;
    float4 c0 = mul(float4(rayOrigin, 1.0), view.CameraVP);
    float4 c1 = mul(float4(rayOrigin + reflDir * maxDist, 1.0), view.CameraVP);

    // Clip ray segment against W >= nearPlane in clip space so projection never flips behind camera.
    float clipTMax = 1.0;
    if (c1.w < nearPlane)
    {
        clipTMax = saturate((nearPlane - c0.w) / (c1.w - c0.w));
        c1 = lerp(c0, c1, clipTMax);
    }

    float2 startUv = (c0.xy / max(c0.w, 1e-5)) * float2(0.5, -0.5) + float2(0.5, 0.5);
    float startZ = saturate(c0.z / max(c0.w, 1e-5));
    float2 endUv = (c1.xy / max(c1.w, 1e-5)) * float2(0.5, -0.5) + float2(0.5, 0.5);
    float endZ = saturate(c1.z / max(c1.w, 1e-5));

    float3 rayStart = float3(startUv, startZ);
    float3 rayDelta = float3(endUv - startUv, endZ - startZ);
    if (dot(rayDelta.xy, rayDelta.xy) < 1e-8)
    {
        outColor[id.xy] = float4(0.0);
        outMeta[id.xy] = float4(uv, 0.0, roughness);
        return;
    }

    uint maxSteps = clamp(uint(root.ScreenAndStepParams.w), 8u, 128u);
    float dt = 1.0 / float(maxSteps);
    float t = dt * (0.35 + 0.65 * bn.x);
    bool useHiZ = root.HiZActive != 0u && root.HiZTex.index != 0u;
    int maxMip = max(int(root.ScreenAndStepParams.z) - 1, 0);
    int mip = useHiZ ? min(2, maxMip) : 0;

    Texture2D<float2> hizTex = gpu_texture<Texture2D<float2>>(root.HiZTex.index);
    float startLinearZ = ssrLinearViewZ(view, startZ);
    float endLinearZ = ssrLinearViewZ(view, endZ);

    bool hitFound = false;
    float hitT = 0.0;
    float prevT = 0.0;

    for (uint iter = 0u; iter < maxSteps && t <= 1.0; ++iter)
    {
        float stepScale = useHiZ ? float(1u << uint(max(mip, 0))) * dt : dt;
        float nextT = min(t + stepScale, 1.0);
        float3 sampleMid = rayStart + rayDelta * t;
        float3 sampleFar = rayStart + rayDelta * nextT;

        if (sampleMid.x < 0.0 || sampleMid.x > 1.0 || sampleMid.y < 0.0 || sampleMid.y > 1.0)
            break;

        if (useHiZ && mip > 0)
        {
            float cellMaxZ = hizTex.SampleLevel(shared_sampler_nearest_clamp(), sampleMid.xy, float(mip)).g;
            float segFarthestZ = min(sampleMid.z, sampleFar.z);
            if (segFarthestZ > cellMaxZ + 1e-5)
            {
                prevT = t;
                t = nextT;
                mip = min(mip + 1, maxMip);
            }
            else
            {
                mip = max(mip - 1, 0);
            }
            continue;
        }

        int2 px = clamp(int2(sampleMid.xy * float2(fullDims)), int2(0, 0), maxFull);
        float sceneDepth = depthTex.Load(int3(px, 0));
        if (sceneDepth > 0.0)
        {
            // Perspective-correct linear depth interpolation along clip ray.
            float rayLinearZ = rcp(lerp(rcp(max(startLinearZ, 0.01)), rcp(max(endLinearZ, 0.01)), t));
            float sceneLinearZ = ssrLinearViewZ(view, sceneDepth);
            float thickness = root.SsrParams.y * (1.0 + t * maxDist * 0.03);
            if (rayLinearZ >= sceneLinearZ && rayLinearZ - sceneLinearZ <= thickness)
            {
                hitFound = true;
                hitT = t;
                break;
            }
        }

        prevT = t;
        t = nextT;
        if (useHiZ)
            mip = min(mip + 1, maxMip);
    }

    if (!hitFound)
    {
        outColor[id.xy] = float4(0.0);
        outMeta[id.xy] = float4(uv, 0.0, roughness);
        return;
    }

    // 4-step binary refinement between prevT and hitT.
    float lo = prevT;
    float hi = hitT;
    [unroll]
    for (uint r = 0u; r < 4u; ++r)
    {
        float midT = 0.5 * (lo + hi);
        float2 midUv = rayStart.xy + rayDelta.xy * midT;
        int2 px = clamp(int2(midUv * float2(fullDims)), int2(0, 0), maxFull);
        float sd = depthTex.Load(int3(px, 0));
        float rayLin = rcp(lerp(rcp(max(startLinearZ, 0.01)), rcp(max(endLinearZ, 0.01)), midT));
        float scnLin = sd > 0.0 ? ssrLinearViewZ(view, sd) : 1.0e8;
        if (rayLin >= scnLin)
            hi = midT;
        else
            lo = midT;
    }

    float2 hitUv = rayStart.xy + rayDelta.xy * hi;
    if (hitUv.x <= 0.0 || hitUv.x >= 1.0 || hitUv.y <= 0.0 || hitUv.y >= 1.0)
    {
        outColor[id.xy] = float4(0.0);
        outMeta[id.xy] = float4(uv, 0.0, roughness);
        return;
    }

    int2 hitPx = clamp(int2(hitUv * float2(fullDims)), int2(0, 0), maxFull);
    float finalSceneDepth = depthTex.Load(int3(hitPx, 0));
    float finalRayLinearZ = rcp(lerp(rcp(max(startLinearZ, 0.01)), rcp(max(endLinearZ, 0.01)), hi));
    float finalSceneLinearZ = finalSceneDepth > 0.0 ? ssrLinearViewZ(view, finalSceneDepth) : 1.0e8;
    float finalThickness = root.SsrParams.y * (1.0 + hi * maxDist * 0.05);
    if (finalSceneDepth <= 0.0 || abs(finalRayLinearZ - finalSceneLinearZ) > finalThickness)
    {
        outColor[id.xy] = float4(0.0);
        outMeta[id.xy] = float4(uv, 0.0, roughness);
        return;
    }

    if (root.RejectBackface != 0u)
    {
        float3 hitNormal = normalize(normalTex.Load(int3(hitPx, 0)).xyz * 2.0 - 1.0);
        if (dot(reflDir, hitNormal) > 0.15)
        {
            outColor[id.xy] = float4(0.0);
            outMeta[id.xy] = float4(uv, 0.0, roughness);
            return;
        }
    }

    Texture2D<float4> hdrIn = gpu_texture<Texture2D<float4>>(root.HdrInTex.index);
    float3 hitColor = hdrIn.SampleLevel(shared_sampler_linear_clamp(), hitUv, 0.0).rgb;

    float edgeFade = max(root.SsrParams.w, 0.01);
    float edgeDist = min(min(hitUv.x, 1.0 - hitUv.x), min(hitUv.y, 1.0 - hitUv.y));
    float borderWeight = smoothstep(0.0, edgeFade, edgeDist);
    float roughWeight = 1.0 - smoothstep(maxRoughness * 0.65, maxRoughness, roughness);
    float distFrac = hi * clipTMax;
    float distWeight = 1.0 - smoothstep(0.75, 1.0, distFrac);
    float confidence = saturate(borderWeight * roughWeight * distWeight);

    outColor[id.xy] = float4(hitColor, confidence);
    outMeta[id.xy] = float4(hitUv, max(distFrac * maxDist, 0.01), roughness);
}
