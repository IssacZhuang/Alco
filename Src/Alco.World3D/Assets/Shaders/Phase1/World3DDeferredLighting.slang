#language slang 2025
module World3DDeferredLighting;

import Core;
import World3DSceneData;
import World3DAtmosphere;

[GPURoot]
public struct DeferredLightingRoot
{
    public GPUBufferView Views;
    public GPUTextureId Albedo;
    public GPUTextureId Normal;
    public GPUTextureId Material;
    public GPUTextureId Emissive;
    public GPUTextureId Depth;
    public GPUTextureId Hdr;
    public float4 SunDirectionAndIntensity;
    public float4 SunColorAndDiscCosRadius;
    public float4 Atmosphere;
    public float4 Sky;
    public float4 SkyHorizonAndDiscBrightness;
    public float4 SkyZenith;
};

// The RGBA8 UNORM albedo attachment is manually sRGB-encoded by the GBuffer pass.
float3 decodeAlbedo(float3 color)
{
    float3 linear = color / 12.92;
    float3 nonlinear = pow((color + 0.055) / 1.055, 2.4);
    return lerp(nonlinear, linear, step(color, float3(0.04045)));
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
    // At depth zero this is a homogeneous direction (w = 0). For geometry w is
    // positive, and cancels when normalizing. Neither path needs a perspective divide.
    float3 ray = normalize(eyeRelativeH.xyz);

    RWTexture2D<float4> hdr = gpu_texture<RWTexture2D<float4>>(root.Hdr.index);
    if (depth == 0.0)
    {
        float3 sky = world3d_sky_color(ray, root.SunDirectionAndIntensity.xyz, root.Atmosphere, root.Sky,
            root.SunColorAndDiscCosRadius.xyz, root.SunColorAndDiscCosRadius.w, root.SkyHorizonAndDiscBrightness.w);
        hdr[id.xy] = float4(sky, 1.0);
        return;
    }

    float3 albedo = decodeAlbedo(loadGBuffer(root.Albedo, pixel).rgb);
    float3 n = normalize(loadGBuffer(root.Normal, pixel).xyz * 2.0 - 1.0);
    float3 material = loadGBuffer(root.Material, pixel).xyz;
    float3 emissive = loadGBuffer(root.Emissive, pixel).rgb;

    float3 direct = evaluateDirectSun(n, -ray, root.SunDirectionAndIntensity.xyz, albedo, material.x, material.y)
        * root.SunColorAndDiscCosRadius.xyz * root.SunDirectionAndIntensity.w;
    float3 indirect = evaluateDiffuseEnvironment(n.z, root.SkyHorizonAndDiscBrightness.xyz, root.SkyZenith.xyz, root.Sky.w)
        * albedo * (1.0 - material.x) * material.z;
    hdr[id.xy] = float4(direct + indirect + emissive, 1.0);
}
