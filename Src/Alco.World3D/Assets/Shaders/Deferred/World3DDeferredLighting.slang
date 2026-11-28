#language slang 2025
module World3DDeferredLighting;

import Core;
import SharedSampler;
import World3DSceneData;
import World3DAtmosphere;
import World3DShadowSample;
import World3DLightClusterCommon;
import World3DPointShadowSample;

[GPURoot]
public struct DeferredLightingRoot
{
    public GPUBufferView Views;
    public GPUBufferView ShadowFrame;
    public GPUTextureId Albedo;
    public GPUTextureId Normal;
    public GPUTextureId Material;
    public GPUTextureId Emissive;
    public GPUTextureId Depth;
    public GPUTextureId Hdr;
    public GPUTextureId GiDiffuse;
    public GPUTextureId GiSpecular;
    public float4 SunDirectionAndIntensity;
    public float4 SunColorAndDiscCosRadius;
    public float4 Atmosphere;
    public float4 Sky;
    public float4 SkyHorizonAndDiscBrightness;
    public float4 SkyZenith;
    public GPUBufferView LocalLighting;
    public GPUTextureId GiRawDiffuse;
    public uint Reserved0;
    public uint GiFlags; // bit 0 = VoxelGI active
    public float GiDiffuseScale;
    public float GiSpecularScale;
    public uint Reserved1;
};

// The RGBA8 UNORM albedo attachment is manually sRGB-encoded by the GBuffer pass.
float3 decodeAlbedo(float3 color)
{
    float3 linear = color / 12.92;
    float3 nonlinear = pow((color + 0.055) / 1.055, 2.4);
    return lerp(nonlinear, linear, step(color, float3(0.04045)));
}

float3 decodeGeometryNormal(float2 encoded)
{
    float2 f = encoded * 2.0 - 1.0;
    float3 gn = float3(f.x, f.y, 1.0 - abs(f.x) - abs(f.y));
    float t = saturate(-gn.z);
    gn.xy += float2(gn.x >= 0.0 ? -t : t, gn.y >= 0.0 ? -t : t);
    float lenSq = dot(gn, gn);
    return lenSq > 0.0 && isfinite(lenSq) ? gn * rsqrt(lenSq) : float3(0.0, 0.0, 1.0);
}

float geometrySchlickGGX(float cosine, float k)
{
    return cosine / (cosine * (1.0 - k) + k + 1e-6);
}

// Cook-Torrance GGX, Schlick Fresnel, and energy-weighted Lambert diffuse.
float3 evaluateDirectSun(float3 n, float3 v, float3 l, float3 albedo, float metallic, float roughness)
{
    float ndotL = saturate(dot(n, l));
    if (ndotL <= 0.0)
        return 0.0;

    float3 halfVector = v + l;
    float3 h = halfVector * rsqrt(max(dot(halfVector, halfVector), 1e-8));
    float ndotV = saturate(dot(n, v));
    float ndotH = saturate(dot(n, h));
    float vdotH = saturate(dot(v, h));

    float3 f0 = lerp(float3(0.04), albedo, metallic);
    float3 fresnel = f0 + (1.0 - f0) * pow(1.0 - vdotH, 5.0);
    float a = roughness * roughness;
    float a2 = a * a;
    float d = ndotH * ndotH * (a2 - 1.0) + 1.0;
    float distribution = a2 / (WORLD3D_PI * d * d + 1e-6);
    float r = roughness + 1.0;
    float k = r * r / 8.0;
    float visibility = geometrySchlickGGX(ndotL, k) * geometrySchlickGGX(ndotV, k);

    float3 specular = distribution * visibility * fresnel / (4.0 * ndotL * ndotV + 1e-6);
    float3 diffuse = (1.0 - fresnel) * (1.0 - metallic) * albedo / WORLD3D_PI;
    return (diffuse + specular) * ndotL;
}

float3 envSpecularBrdfApprox(float3 f0, float roughness, float ndotV)
{
    const float4 c0 = float4(-1.0, -0.0275, -0.572, 0.022);
    const float4 c1 = float4(1.0, 0.0425, 1.04, -0.04);
    float4 r = roughness * c0 + c1;
    float a004 = min(r.x * r.x, exp2(-9.28 * ndotV)) * r.x + r.y;
    float2 ab = float2(-1.04, 1.04) * a004 + r.zw;
    return f0 * ab.x + ab.y;
}

float3 evaluateLocalLights(
    World3DLocalLightingFrameGpu localLighting,
    uint2 pixel,
    float viewDepth,
    float3 eyeRelPos,
    float3 n,
    float3 geometryNormal,
    float3 v,
    float3 albedo,
    float metallic,
    float roughness)
{
    float3 sum = float3(0.0);
    World3DLightClusterIterator iterator = world3d_begin_local_lights(localLighting.Clusters, pixel, viewDepth);
    World3DClusterLightGpu light;
    while (world3d_try_next_local_light(localLighting.Clusters, iterator, light))
    {
        float3 delta = light.EyeRelativePositionAndRadius.xyz - eyeRelPos;
        float distSq = dot(delta, delta);
        float radius = light.EyeRelativePositionAndRadius.w;
        float radiusSq = radius * radius;
        if (distSq >= radiusSq)
            continue;

        float3 l = delta * rsqrt(max(distSq, 1e-6));
        if (dot(n, l) <= 0.0)
            continue;

        float visibility = world3d_point_shadow_visibility(localLighting.PointShadows, light.ShadowIndexPlusOne, -delta,
            geometryNormal, light.SourceRadius);
        float win = saturate(1.0 - distSq / radiusSq);
        win *= win;
        float atten = win / (distSq + 0.1);
        sum += evaluateDirectSun(n, v, l, albedo, metallic, roughness)
            * (light.ColorAndIntensity.rgb * light.ColorAndIntensity.w * atten * visibility);
    }
    return sum;
}

// Unit-albedo Lambert response to the CPU-filtered sky, plus a small ground/sky bounce floor.
float3 evaluateDiffuseEnvironment(float up, float3 horizon, float3 zenith, float ambientFloor)
{
    float3 sideResponse = horizon * 0.218505 + zenith * 0.281495;
    float3 upResponse = horizon * 0.230769 + zenith * 0.769231;
    float3 sky = lerp(sideResponse, upResponse, saturate(up)) * (1.0 - saturate(-up));
    float3 bounce = lerp(float3(0.05, 0.045, 0.04), float3(0.10, 0.12, 0.15), saturate(up * 0.5 + 0.5));
    return sky + ambientFloor * bounce;
}

float4 loadGBuffer(GPUTextureId id, int3 pixel)
{
    Texture2D<float4> texture = gpu_texture<Texture2D<float4>>(id.index);
    return texture.Load(pixel);
}

float3 cascadeDebugColor(int cascadeIndex)
{
    if (cascadeIndex == 0) return float3(0.15, 0.85, 0.25);
    if (cascadeIndex == 1) return float3(0.95, 0.85, 0.15);
    if (cascadeIndex == 2) return float3(0.95, 0.45, 0.10);
    if (cascadeIndex == 3) return float3(0.90, 0.15, 0.20);
    if (cascadeIndex == 4) return float3(0.80, 0.20, 0.90);
    return float3(0.20, 0.70, 0.95);
}

[shader("compute")]
[numthreads(8, 8, 1)]
void MainCS(uint3 id : SV_DispatchThreadID)
{
    DeferredLightingRoot root = gpu_load_root<DeferredLightingRoot>();
    World3DView view = gpu_load<World3DView>(root.Views, 0);
    if (any(id.xy >= uint2(view.ViewportSizeInvSize.xy)))
        return;

    int3 pixel = int3(id.xy, 0);
    Texture2D<float> depthTexture = gpu_texture<Texture2D<float>>(root.Depth.index);
    float depth = depthTexture.Load(pixel);
    float2 uv = (float2(id.xy) + 0.5) * view.ViewportSizeInvSize.zw;
    float2 ndc = uv * float2(2.0, -2.0) + float2(-1.0, 1.0);
    float4 eyeRelativeH = mul(float4(ndc, depth, 1.0), view.CameraInvVP);
    float3 ray = normalize(eyeRelativeH.xyz);

    World3DShadowFrameGpu shadowFrame = gpu_load<World3DShadowFrameGpu>(root.ShadowFrame, 0);
    RWTexture2D<float4> hdr = gpu_texture<RWTexture2D<float4>>(root.Hdr.index);

    if (shadowFrame.DebugView == 5u && (shadowFrame.Flags & WORLD3D_SHADOW_FLAG_HIZ_ACTIVE) != 0u && shadowFrame.HiZTex.index != 0u)
    {
        Texture2D<float2> hizTex = gpu_texture<Texture2D<float2>>(shadowFrame.HiZTex.index);
        float2 hz = hizTex.SampleLevel(shared_sampler_nearest_clamp(), uv, 0.0);
        hdr[id.xy] = float4(saturate(hz.r * 16.0), saturate(hz.g * 16.0), depth > 0.0 ? 0.15 : 0.0, 1.0);
        return;
    }

    if (shadowFrame.DebugView == 6u && (shadowFrame.Flags & WORLD3D_SHADOW_FLAG_RSM_ACTIVE) != 0u && shadowFrame.RsmFluxTex.index != 0u)
    {
        Texture2D<float4> rsmTex = gpu_texture<Texture2D<float4>>(shadowFrame.RsmFluxTex.index);
        hdr[id.xy] = float4(rsmTex.SampleLevel(shared_sampler_linear_clamp(), uv, 0.0).rgb, 1.0);
        return;
    }

    if (depth == 0.0)
    {
        float3 sky = world3d_sky_color(ray, root.SunDirectionAndIntensity.xyz, root.Atmosphere, root.Sky,
            root.SunColorAndDiscCosRadius.xyz, root.SunColorAndDiscCosRadius.w, root.SkyHorizonAndDiscBrightness.w);
        hdr[id.xy] = float4(sky, 1.0);
        return;
    }

    float4 normalSample = loadGBuffer(root.Normal, pixel);
    float4 emissiveSample = loadGBuffer(root.Emissive, pixel);
    float3 albedo = decodeAlbedo(loadGBuffer(root.Albedo, pixel).rgb);
    float3 n = normalize(normalSample.xyz * 2.0 - 1.0);
    float3 geometryNormal = decodeGeometryNormal(float2(normalSample.a, emissiveSample.a));
    float3 material = loadGBuffer(root.Material, pixel).xyz;
    float3 emissive = emissiveSample.rgb;

    float3 eyeRelPos = eyeRelativeH.xyz / max(eyeRelativeH.w, 1e-6);
    World3DShadowEval shadowEval = world3d_evaluate_sun_shadow(shadowFrame, view, eyeRelPos, geometryNormal);

    if (shadowFrame.DebugView == 1u)
    {
        float3 dbg = cascadeDebugColor(shadowEval.CascadeIndex) * (0.35 + 0.65 * shadowEval.Visibility);
        hdr[id.xy] = float4(dbg, 1.0);
        return;
    }
    if (shadowFrame.DebugView == 2u)
    {
        hdr[id.xy] = float4(float3(shadowEval.Visibility), 1.0);
        return;
    }
    if (shadowFrame.DebugView == 3u)
    {
        float3 mmColor = shadowEval.MinMaxState == 1u ? float3(0.15, 0.85, 0.25)
            : (shadowEval.MinMaxState == 2u ? float3(0.90, 0.15, 0.15)
            : (shadowEval.MinMaxState == 3u ? float3(0.95, 0.85, 0.15) : float3(0.25, 0.35, 0.65)));
        hdr[id.xy] = float4(mmColor * (0.4 + 0.6 * shadowEval.Visibility), 1.0);
        return;
    }
    if (shadowFrame.DebugView == 4u)
    {
        float b = shadowEval.FarBlendWeight;
        hdr[id.xy] = float4(lerp(float3(0.15, 0.70, 0.30), float3(0.90, 0.30, 0.85), b) * (0.4 + 0.6 * shadowEval.Visibility), 1.0);
        return;
    }

    bool giActive = (root.GiFlags & 1u) != 0u && root.GiDiffuse.index != 0u;
    float4 giDiff = float4(0.0, 0.0, 0.0, 1.0);
    float4 giSpec = float4(0.0);
    if (giActive)
    {
        giDiff = loadGBuffer(root.GiDiffuse, pixel);
        giSpec = loadGBuffer(root.GiSpecular, pixel);
    }

    if (shadowFrame.DebugView == 7u)
    {
        hdr[id.xy] = float4(giActive ? giDiff.rgb * root.GiDiffuseScale : float3(0.0), 1.0);
        return;
    }
    if (shadowFrame.DebugView == 8u)
    {
        hdr[id.xy] = float4(giActive ? giSpec.rgb * root.GiSpecularScale : float3(0.0), 1.0);
        return;
    }
    if (shadowFrame.DebugView == 9u)
    {
        hdr[id.xy] = float4(float3(giActive ? giDiff.a : 1.0), 1.0);
        return;
    }
    if (shadowFrame.DebugView == 10u)
    {
        float3 rawDiff = (giActive && root.GiRawDiffuse.index != 0u)
            ? gpu_texture<Texture2D<float4>>(root.GiRawDiffuse.index).SampleLevel(shared_sampler_linear_clamp(), uv, 0.0).rgb
            : float3(0.0);
        hdr[id.xy] = float4(rawDiff * root.GiDiffuseScale, 1.0);
        return;
    }

    float3 v = -ray;
    float3 direct = evaluateDirectSun(n, v, root.SunDirectionAndIntensity.xyz, albedo, material.x, material.y)
        * root.SunColorAndDiscCosRadius.xyz * root.SunDirectionAndIntensity.w * shadowEval.Visibility;
    if (root.LocalLighting.Index != 0u)
    {
        World3DLocalLightingFrameGpu localLighting = gpu_load<World3DLocalLightingFrameGpu>(root.LocalLighting);
        direct += evaluateLocalLights(localLighting, id.xy, view.EyeLocalNear.w / depth,
            eyeRelPos, n, geometryNormal, v, albedo, material.x, material.y);
    }

    float3 indirect;
    if (giActive)
    {
        float roughness = clamp(material.y, 0.02, 1.0);
        float ndotV = saturate(dot(n, v));
        float3 f0 = lerp(float3(0.04), albedo, material.x);
        float3 envBrdf = envSpecularBrdfApprox(f0, roughness, ndotV);
        float3 kD = (1.0 - saturate(envBrdf)) * (1.0 - material.x);
        float3 giDiffuseTerm = giDiff.rgb * root.GiDiffuseScale * albedo * kD * material.z;
        float3 giSpecularTerm = giSpec.rgb * root.GiSpecularScale * envBrdf * material.z;
        indirect = giDiffuseTerm + giSpecularTerm;
    }
    else
    {
        indirect = evaluateDiffuseEnvironment(n.z, root.SkyHorizonAndDiscBrightness.xyz, root.SkyZenith.xyz, root.Sky.w)
            * albedo * (1.0 - material.x) * material.z;
    }
    hdr[id.xy] = float4(direct + indirect + emissive, 1.0);
}
