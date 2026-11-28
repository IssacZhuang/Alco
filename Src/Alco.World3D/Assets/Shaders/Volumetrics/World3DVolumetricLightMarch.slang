#language slang 2025
module World3DVolumetricLightMarch;

import Core;
import World3DSceneData;
import World3DShadowSample;
import World3DAtmosphere;
import World3DCloudCommon;

[GPURoot]
public struct VolumetricLightMarchRoot
{
    public float4 FogParams;        // x = baseDensity, y = heightFalloff, z = baseHeightMeters, w = maxDistanceMeters
    public float4 LightParams;      // rgb = sunColor * intensity, w = anisotropy
    public float4 AmbientAndAlt;    // rgb = ambientFogColor, w = eyeAltitudeMeters
    public float4 AtmosphereParams; // Atmosphere constants for beam transmittance
    public GPUBufferView Views;
    public GPUBufferView ShadowFrame;
    public GPUTextureId DepthTex;
    public GPUTextureId OutScatterTex;
    public uint TraceWidth;
    public uint TraceHeight;
    public uint StepCount;
    public uint FrameIndex;
    public uint Reserved0;
    public uint Reserved1;
};

float volLightIgn(float2 pixel, uint frameIndex)
{
    float2 p = pixel + float2(float(frameIndex & 31u) * 5.588238, float((frameIndex >> 2) & 31u) * 3.173921);
    return frac(52.9829189 * frac(dot(p, float2(0.06711056, 0.00583715))));
}

[shader("compute")]
[numthreads(8, 8, 1)]
public void csMain(uint3 id : SV_DispatchThreadID)
{
    VolumetricLightMarchRoot root = gpu_load_root<VolumetricLightMarchRoot>();
    if (id.x >= root.TraceWidth || id.y >= root.TraceHeight)
        return;

    World3DView view = gpu_load<World3DView>(root.Views, 0);
    World3DShadowFrameGpu shadowFrame = gpu_load<World3DShadowFrameGpu>(root.ShadowFrame, 0);
    Texture2D<float> depthTex = gpu_texture<Texture2D<float>>(root.DepthTex.index);
    RWTexture2D<float4> outScatter = gpu_texture<RWTexture2D<float4>>(root.OutScatterTex.index);

    uint2 fullDims = max(uint2(view.ViewportSizeInvSize.xy), uint2(1u));
    int2 maxFull = int2(fullDims) - int2(1, 1);
    float2 uv = (float2(id.xy) + 0.5) / float2(float(root.TraceWidth), float(root.TraceHeight));
    int2 depthCoord = clamp(int2(uv * float2(fullDims)), int2(0, 0), maxFull);
    float depth = depthTex.Load(int3(depthCoord, 0));

    float3 ray = cloud_reconstruct_ray(view, uv);
    float maxDist = root.FogParams.w;
    if (depth > 0.0)
    {
        float2 ndc = uv * float2(2.0, -2.0) + float2(-1.0, 1.0);
        float4 eyePosH = mul(float4(ndc, depth, 1.0), view.CameraInvVP);
        float sceneDist = eyePosH.w > 1e-6 ? length(eyePosH.xyz / eyePosH.w) : cloud_linear_view_z(view, depth);
        maxDist = min(maxDist, sceneDist);
    }

    if (maxDist <= 0.05)
    {
        outScatter[id.xy] = float4(0.0, 0.0, 0.0, 1.0);
        return;
    }

    uint steps = clamp(root.StepCount, 4u, 64u);
    float jitter = volLightIgn(float2(id.xy), root.FrameIndex);

    float3 sunDir = normalize(shadowFrame.SunDirectionParams.xyz);
    float cosTheta = clamp(dot(ray, sunDir), -1.0, 1.0);
    float phase = cloud_hg_phase(cosTheta, clamp(root.LightParams.w, -0.95, 0.95));

    float3 scatter = float3(0.0);
    float transmittance = 1.0;
    float prevT = 0.0;

    for (uint i = 0u; i < steps; ++i)
    {
        float s0 = float(i) / float(steps);
        float s1 = float(i + 1u) / float(steps);
        float t0 = maxDist * s0 * s0;
        float t1 = maxDist * s1 * s1;
        float ds = max(t1 - t0, 1e-4);
        float t = lerp(t0, t1, jitter);
        prevT = t1;

        float3 eyeRelPos = ray * t;
        float worldAlt = root.AmbientAndAlt.w + eyeRelPos.z;
        float h = max(worldAlt - root.FogParams.z, 0.0);
        float density = root.FogParams.x * exp(-h * root.FogParams.y);
        if (density <= 1e-6)
            continue;

        float shadowVis = world3d_sample_sun_shadow_volumetric(shadowFrame, view, eyeRelPos);
        float3 sampleLight = root.LightParams.rgb * (phase * shadowVis) + root.AmbientAndAlt.rgb;
        float segTrans = exp(-density * ds);
        float weight = (1.0 - segTrans);
        scatter += transmittance * sampleLight * weight;
        transmittance *= segTrans;
        if (transmittance < 0.01)
        {
            transmittance = 0.0;
            break;
        }
    }

    outScatter[id.xy] = float4(scatter, transmittance);
}
