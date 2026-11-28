#language slang 2025
module World3DForward;

import Core;
import SharedSampler;
import World3DSceneData;
import World3DSurface;
import World3DShadowSample;
import World3DAtmosphere;

[GPURoot]
public struct World3DForwardRoot
{
    public float4 SunDirectionAndIntensity;
    public float4 SunColorAndAmbient;       // rgb = sunColor, w = ambientFloor
    public float4 SkyHorizonAndRefrScale;   // rgb = skyHorizon, w = refractionScale
    public float4 SkyZenithAndGiScale;      // rgb = skyZenith, w = giDiffuseScale
    public GPUBufferView Directory;
    public GPUBufferView Views;
    public GPUBufferView DrawRefs;
    public GPUBufferView ShadowFrame;
    public GPUBufferView PointLights;
    public GPUTextureId DepthTex;
    public GPUTextureId ColorSnapshotTex;
    public GPUTextureId GiDiffuseTex;
    public uint FirstDraw;
    public uint PointLightCount;
    public uint Flags;                      // bit 0 = EnableRefraction, bit 1 = GiActive
};

struct ForwardDrawRef
{
    uint InstanceSlot;
    uint Lod;
};

public struct ForwardVaryings
{
    public float4 Position : SV_Position;
    public float3 EyeRelativePosition : TEXCOORD0;
    public float3 NormalWS : TEXCOORD1;
    public float4 TangentWS : TEXCOORD2;
    public float2 UV : TEXCOORD3;
    public nointerpolation uint MaterialSlot : TEXCOORD4;
    public nointerpolation float Fade : TEXCOORD5;
    public nointerpolation uint Mirrored : TEXCOORD6;
};

float3 normalizeOr(float3 direction, float3 fallback)
{
    float lenSq = dot(direction, direction);
    return lenSq > 0.0 && isfinite(lenSq) ? direction * rsqrt(lenSq) : fallback;
}

float3 transformNormal(float3 normal, float3x3 linear, out bool mirrored)
{
    float3 cx = cross(linear[1], linear[2]);
    float3 cy = cross(linear[2], linear[0]);
    float3 cz = cross(linear[0], linear[1]);
    mirrored = dot(linear[0], cx) < 0.0;
    float3 transformed = normal.x * cx + normal.y * cy + normal.z * cz;
    return normalizeOr(mirrored ? -transformed : transformed, float3(0.0, 0.0, 1.0));
}

float3 orthogonalizeTangent(float3 tangent, float3 normal)
{
    tangent -= normal * dot(normal, tangent);
    float lenSq = dot(tangent, tangent);
    if (lenSq > 0.0 && isfinite(lenSq))
        return tangent * rsqrt(lenSq);
    float3 axis = abs(normal.z) < 0.999 ? float3(0.0, 0.0, 1.0) : float3(0.0, 1.0, 0.0);
    return normalize(cross(axis, normal));
}

[shader("vertex")]
#ifdef ALCO_GRAPHICS_DRAW_INDEX
public ForwardVaryings vsMain<T : ISurface>(uint vertexId : SV_VertexID, uint drawId : SV_DrawIndex)
{
#else
public ForwardVaryings vsMain<T : ISurface>(uint vertexId : SV_VertexID)
{
    uint drawId = 0u;
#endif
    World3DForwardRoot root = gpu_load_root<World3DForwardRoot>();
    World3DView view = gpu_load<World3DView>(root.Views, 0u);
    ForwardDrawRef draw = gpu_load<ForwardDrawRef>(root.DrawRefs, root.FirstDraw + drawId);
    World3DInstanceGpu instance = world3d_load_instance(root.Directory, draw.InstanceSlot);
    World3DGeometryLodGpu lod = world3d_load_geometry_lod(root.Directory, instance.GeometrySlot, draw.Lod);

    GPUBufferView vertices;
    GPUBufferView indices;
    if (!world3d_try_get_geometry_views(root.Directory, lod, vertices, indices))
        return (ForwardVaryings)0;

    uint idx = world3d_load_index(indices, vertexId);
    World3DVertexGpu vertex = world3d_load_vertex(vertices, idx);

    float3 eyeRelativePosition = world3d_eye_relative_position(instance.Spatial, view, vertex.Position);
    float3x3 linear = (float3x3)instance.Spatial.ObjectToCell;
    bool mirrored;
    float3 normal = transformNormal(vertex.Normal, linear, mirrored);
    float3 tangent = mul(vertex.Tangent.xyz, linear);

    T surface = T();
    ForwardVaryings output;
    output.EyeRelativePosition = surface.getWorldPosition(eyeRelativePosition, normal, vertex.UV);
    output.Position = mul(float4(output.EyeRelativePosition, 1.0), view.CameraVP);
    output.NormalWS = surface.getWorldNormal(eyeRelativePosition, normal, vertex.UV);
    output.TangentWS = float4(tangent, mirrored ? -vertex.Tangent.w : vertex.Tangent.w);
    output.UV = vertex.UV;
    output.MaterialSlot = instance.MaterialSlot;
    output.Fade = instance.Fade;
    output.Mirrored = mirrored ? 1u : 0u;
    return output;
}

float distributionGgx(float ndotH, float roughness)
{
    float a = max(roughness * roughness, 0.002);
    float a2 = a * a;
    float d = ndotH * ndotH * (a2 - 1.0) + 1.0;
    return a2 / max(WORLD3D_PI * d * d, 1e-6);
}

float geometrySmith(float ndotV, float ndotL, float roughness)
{
    float r = roughness + 1.0;
    float k = (r * r) * 0.125;
    float gv = ndotV / max(ndotV * (1.0 - k) + k, 1e-5);
    float gl = ndotL / max(ndotL * (1.0 - k) + k, 1e-5);
    return gv * gl;
}

float3 fresnelSchlick(float cosTheta, float3 f0)
{
    float m = saturate(1.0 - cosTheta);
    float m2 = m * m;
    return f0 + (1.0 - f0) * (m2 * m2 * m);
}

float3 sampleRefractedBackground(
    World3DForwardRoot root,
    World3DView view,
    float2 screenUv,
    float pxDepth,
    float3 eyeRelPos,
    float3 n,
    float3 v,
    float ior,
    float roughness)
{
    Texture2D<float4> bgTex = gpu_texture<Texture2D<float4>>(root.ColorSnapshotTex.index);
    Texture2D<float> depthTex = gpu_texture<Texture2D<float>>(root.DepthTex.index);
    SamplerState clampSampler = shared_sampler_linear_clamp();

    float3 refrDir = refract(-v, n, 1.0 / max(ior, 1.0));
    if (dot(refrDir, refrDir) < 1e-4)
        refrDir = -v;

    float viewDist = clamp(length(eyeRelPos), 0.4, 25.0);
    float bendScale = viewDist * root.SkyHorizonAndRefrScale.w * clamp(ior - 1.0, 0.0, 2.0);
    float3 displacedPos = eyeRelPos + (refrDir + v) * bendScale;
    float4 dispClip = mul(float4(displacedPos, 1.0), view.CameraVP);

    float2 texel = view.ViewportSizeInvSize.zw;
    float2 refrUv = screenUv;
    if (dispClip.w > 1e-4)
    {
        float2 candidateUv = (dispClip.xy / dispClip.w) * float2(0.5, -0.5) + float2(0.5, 0.5);
        candidateUv = clamp(candidateUv, texel * 0.5, 1.0 - texel * 0.5);
        float bgDepth = depthTex.SampleLevel(shared_sampler_nearest_clamp(), candidateUv, 0.0).r;
        // Reversed-Z: bgDepth > pxDepth means a foreground opaque object sits closer than the glass.
        if (bgDepth <= pxDepth + 1e-5)
            refrUv = candidateUv;
    }

    float blurRadius = clamp(roughness, 0.0, 1.0) * 0.004;
    if (blurRadius <= 1e-4)
        return bgTex.SampleLevel(clampSampler, refrUv, 0.0).rgb;

    const float2 offsets[4] = {
        float2(-1.0, -0.5), float2( 1.0,  0.5),
        float2(-0.5,  1.0), float2( 0.5, -1.0)
    };
    float3 accum = bgTex.SampleLevel(clampSampler, refrUv, 0.0).rgb;
    float weight = 1.0;
    [unroll]
    for (uint i = 0u; i < 4u; ++i)
    {
        float2 suv = clamp(refrUv + offsets[i] * blurRadius, texel * 0.5, 1.0 - texel * 0.5);
        float sd = depthTex.SampleLevel(shared_sampler_nearest_clamp(), suv, 0.0).r;
        if (sd <= pxDepth + 1e-5)
        {
            accum += bgTex.SampleLevel(clampSampler, suv, 0.0).rgb;
            weight += 1.0;
        }
    }
    return accum / weight;
}

[shader("pixel")]
public float4 psMain<T : ISurface>(ForwardVaryings vertex, bool front : SV_IsFrontFace) : SV_Target0
{
    World3DForwardRoot root = gpu_load_root<World3DForwardRoot>();
    SurfaceInput input;
    input.EyeRelativePosition = vertex.EyeRelativePosition;
    input.UV = vertex.UV;
    input.DdxUV = ddx(vertex.UV);
    input.DdyUV = ddy(vertex.UV);
    input.Material = world3d_load_material(root.Directory, vertex.MaterialSlot);

    bool isBackFace = front != (vertex.Mirrored != 0u);
    bool doubleSided = (input.Material.Flags & WORLD3D_MATERIAL_DOUBLE_SIDED) != 0u;
    if (!doubleSided && isBackFace)
        discard;

    float3 n = normalizeOr(vertex.NormalWS, float3(0.0, 0.0, 1.0));
    float3 t = orthogonalizeTangent(vertex.TangentWS.xyz, n);
    float handedness = vertex.TangentWS.w < 0.0 ? -1.0 : 1.0;
    if (doubleSided && isBackFace)
    {
        n = -n;
        t = -t;
        handedness = -handedness;
    }
    input.NormalWS = n;
    input.TangentWS = float4(t, handedness);

    T surface = T();
    float4 baseColor = surface.getBaseColor(input);
    float3 mrAO = surface.getMetallicRoughnessAO(input);
    float3 normalTS = surface.getNormalTS(input);
    float3 emissive = surface.getEmissive(input);
    float3 b = cross(n, t) * handedness;
    n = normalizeOr(t * normalTS.x + b * normalTS.y + n * normalTS.z, n);

    World3DView view = gpu_load<World3DView>(root.Views, 0u);
    World3DShadowFrameGpu shadowFrame = gpu_load<World3DShadowFrameGpu>(root.ShadowFrame, 0u);
    float3 v = normalizeOr(-vertex.EyeRelativePosition, float3(0.0, 0.0, 1.0));
    float ndotV = max(dot(n, v), 1e-4);

    float metallic = saturate(mrAO.x);
    float roughness = clamp(mrAO.y, 0.04, 1.0);
    float ao = mrAO.z;

    float transmission = world3d_material_transmission(input.Material);
    float ior = world3d_material_ior(input.Material);
    float dielecF0 = (1.0 - ior) / (1.0 + ior);
    dielecF0 *= dielecF0;
    float3 f0 = lerp(float3(dielecF0), baseColor.rgb, metallic);
    float3 fresnel = fresnelSchlick(ndotV, f0);

    float3 sunDir = normalize(root.SunDirectionAndIntensity.xyz);
    World3DShadowEval shadowEval = world3d_evaluate_sun_shadow(shadowFrame, view, vertex.EyeRelativePosition, n);

    float ndotL = saturate(dot(n, sunDir));
    float3 hVec = normalizeOr(sunDir + v, n);
    float ndotH = saturate(dot(n, hVec));
    float hdotV = saturate(dot(hVec, v));
    float3 Fsun = fresnelSchlick(hdotV, f0);
    float D = distributionGgx(ndotH, roughness);
    float G = geometrySmith(ndotV, ndotL, roughness);
    float3 specBrdf = (D * G * Fsun) / max(4.0 * ndotV * ndotL, 1e-4);
    float3 kD = (1.0 - Fsun) * (1.0 - metallic);
    float3 sunLight = root.SunColorAndAmbient.rgb * (root.SunDirectionAndIntensity.w * shadowEval.Visibility * ndotL);
    float3 directDiffuse = kD * baseColor.rgb * (1.0 / WORLD3D_PI) * sunLight;
    float3 directSpec = specBrdf * sunLight;

    for (uint li = 0u; li < root.PointLightCount; ++li)
    {
        World3DPointLightGpu light = gpu_load<World3DPointLightGpu>(root.PointLights, li);
        float3 lightRelEye = float3(light.Cell.xyz - view.EyeCell.xyz) * WORLD3D_CELL_SIZE
            + (light.LocalPositionAndRadius.xyz - view.EyeLocalNear.xyz);
        float3 delta = lightRelEye - vertex.EyeRelativePosition;
        float distSq = dot(delta, delta);
        float radius = max(light.LocalPositionAndRadius.w, 0.01);
        float radiusSq = radius * radius;
        if (distSq >= radiusSq)
            continue;

        float win = saturate(1.0 - distSq / radiusSq);
        win *= win;
        float atten = win / (distSq + 0.25);
        float3 lDir = delta * rsqrt(max(distSq, 1e-6));
        float pNdotL = saturate(dot(n, lDir));
        float3 pH = normalizeOr(lDir + v, n);
        float pNdotH = saturate(dot(n, pH));
        float pHdotV = saturate(dot(pH, v));
        float3 pF = fresnelSchlick(pHdotV, f0);
        float pD = distributionGgx(pNdotH, roughness);
        float pG = geometrySmith(ndotV, pNdotL, roughness);
        float3 pSpec = (pD * pG * pF) / max(4.0 * ndotV * pNdotL, 1e-4);
        float3 pKd = (1.0 - pF) * (1.0 - metallic);
        float3 pRadi = light.ColorAndIntensity.rgb * (light.ColorAndIntensity.w * atten * pNdotL);
        directDiffuse += pKd * baseColor.rgb * (1.0 / WORLD3D_PI) * pRadi;
        directSpec += pSpec * pRadi;
    }

    float2 screenUv = vertex.Position.xy * view.ViewportSizeInvSize.zw;
    float skyHemi = saturate(n.z * 0.5 + 0.5);
    float3 skyAmbient = lerp(root.SkyHorizonAndRefrScale.rgb * root.SunColorAndAmbient.w,
        root.SkyZenithAndGiScale.rgb, skyHemi);
    if ((root.Flags & 2u) != 0u && root.GiDiffuseTex.index != 0u)
    {
        float4 giDiff = gpu_texture<Texture2D<float4>>(root.GiDiffuseTex.index)
            .SampleLevel(shared_sampler_linear_clamp(), screenUv, 0.0);
        skyAmbient = giDiff.rgb * root.SkyZenithAndGiScale.w;
    }
    float3 indirectDiffuse = skyAmbient * baseColor.rgb * (1.0 - metallic) * ao;
    float3 reflDir = reflect(-v, n);
    float3 envRefl = lerp(root.SkyHorizonAndRefrScale.rgb, root.SkyZenithAndGiScale.rgb, saturate(reflDir.z * 0.5 + 0.5))
        * (1.0 - roughness * 0.7) * fresnel;

    float fade = saturate(vertex.Fade);
    float alphaCov = saturate(baseColor.a * fade);
    bool useRefraction = (root.Flags & 1u) != 0u && transmission > 0.001 && root.ColorSnapshotTex.index != 0u;

    if (useRefraction)
    {
        float glassCov = saturate(transmission * fade);
        float3 bgColor = sampleRefractedBackground(root, view, screenUv, vertex.Position.z,
            vertex.EyeRelativePosition, n, v, ior, roughness);
        float3 glassTint = lerp(float3(1.0), baseColor.rgb, saturate(baseColor.a * 0.65 + 0.15));
        float3 transmitted = bgColor * glassTint * (1.0 - fresnel) * glassCov;
        float3 solidLayer = (directDiffuse + indirectDiffuse) * (alphaCov * (1.0 - glassCov));
        float3 specLayer = (directSpec + envRefl) * saturate(max(alphaCov, fresnel.x) * fade + (1.0 - fade) * 0.0);
        float3 premulRgb = transmitted + solidLayer + specLayer * fade + emissive * fade;
        float outAlpha = lerp(alphaCov, 1.0, glassCov);
        return float4(premulRgb, saturate(outAlpha));
    }

    float3 litDiffuse = (directDiffuse + indirectDiffuse) * alphaCov;
    float3 litSpec = (directSpec + envRefl) * saturate(max(alphaCov, dot(fresnel, float3(0.3333)) * fade));
    float outStdAlpha = saturate(max(alphaCov, fresnel.x * fade * step(0.001, transmission)));
    return float4(litDiffuse + litSpec + emissive * fade, outStdAlpha);
}
