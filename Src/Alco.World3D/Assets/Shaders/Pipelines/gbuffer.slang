// G-buffer pass template for surface materials of the deferred pipeline.
// Slang counterpart of Shaders/Pipelines/Rendering/PBR/GBuffer.hlsl.
//
// A pass template is a self-describing module: it owns the [shader] entry
// points, generic over the surface type. The material compiler composes this
// module with a material's surface module through slang's component system
// (composite + link-time specialization with the surface's `Surface` type) -
// no generated wrapper code. Slang type-checks the surface against ISurface
// (Shaders/Libs/alco-world3d-surface.slang) at instantiation.
//
// Writes albedo, world-space normal (from the surface's tangent-space normal
// via TBN), material and emissive data. The interpolated mesh normal is
// octahedrally packed into two spare channels for stable diffuse GI. The
// vertex layout must match Alco.Rendering.VertexPBR exactly. All per-item
// data (model matrix, factors, alpha cutoff) lives in the _instances storage
// buffer and is fetched by SV_VulkanInstanceID; the pixel shader re-reads it
// through the instance id interpolant.

#language slang 2025
module gbuffer;

import alco_world3d_surface;
import pbr_instance;

public struct GBufferVertex
{
    float3 position : POSITION;
    float3 normal : NORMAL;
    float2 uv : TEXCOORD0;
    float4 tangent : TANGENT; // xyz = tangent, w = bitangent sign
    uint instanceID : SV_VulkanInstanceID;
}

public struct GBufferV2F
{
    float4 position : SV_POSITION;
    float3 normal : TEXCOORD0;
    float2 uv : TEXCOORD1;
    float4 tangent : TEXCOORD2; // xyz = world tangent, w = bitangent sign
    uint instanceID : TEXCOORD3;
    float3 worldPos : TEXCOORD4;
}

public struct CameraParams
{
    public float4x4 viewProjection;
};

public ParameterBlock<CameraParams> _camera;

public struct DrawParams
{
    public RWStructuredBuffer<PbrInstance> _instances;
};

public ParameterBlock<DrawParams> _draw;

// Octahedral encoding keeps the interpolated mesh normal in the two half-float
// alpha channels of the normal and emissive G-buffer targets.
float2 EncodeGeometryNormal(float3 normal)
{
    normal /= max(abs(normal.x) + abs(normal.y) + abs(normal.z), 0.0001);
    float2 encoded = normal.xy;
    if (normal.z < 0.0)
    {
        encoded = (1.0 - abs(encoded.yx))
            * float2(encoded.x >= 0.0 ? 1.0 : -1.0,
                     encoded.y >= 0.0 ? 1.0 : -1.0);
    }
    return encoded * 0.5 + 0.5;
}

// Linear RGB to sRGB encoding (the albedo target is RGBA8Unorm).
float3 EncodeSRGB(float3 color)
{
    float3 lo = color * 12.92;
    float3 hi = 1.055 * pow(max(color, 0.0), float3(1.0 / 2.4)) - 0.055;
    return lerp(hi, lo, step(color, float3(0.0031308, 0.0031308, 0.0031308)));
}

[shader("vertex")]
public GBufferV2F MainVS<T : ISurface>(GBufferVertex input)
{
    GBufferV2F output;
    PbrInstance inst = _draw._instances[input.instanceID];
    // Rigid transform only (uniform scale); fine for the demo scene.
    float3 worldPos = mul(inst.model, float4(input.position, 1.0f)).xyz;
    float3 worldNormal = mul((float3x3)inst.model, input.normal);
    float3 worldTangent = mul((float3x3)inst.model, input.tangent.xyz);
    // The surface may deform the vertex; every pass applies this identically
    // so G-buffer, shadows and GI stay consistent.
    T surface = T();
    surface.ModifyVertex(worldPos, worldNormal, input.uv);
    output.position = mul(_camera.viewProjection, float4(worldPos, 1.0f));
    output.normal = worldNormal;
    output.tangent = float4(worldTangent, input.tangent.w);
    output.uv = input.uv;
    output.instanceID = input.instanceID;
    output.worldPos = worldPos;
    return output;
}

[shader("fragment")]
public void MainPS<T : ISurface>(
    GBufferV2F input,
    out float4 albedoRT : SV_TARGET0,
    out float4 normalRT : SV_TARGET1,
    out float4 mrAORT : SV_TARGET2,
    out float4 emissiveRT : SV_TARGET3)
{
    PbrInstance inst = _draw._instances[input.instanceID];

    // TBN frame: re-orthogonalize the interpolated tangent against the normal.
    float3 n = normalize(input.normal);
    float3 t = input.tangent.xyz - n * dot(n, input.tangent.xyz);
    t = normalize(t);
    float3 b = cross(n, t) * input.tangent.w;

    SurfaceInput surfaceInput;
    surfaceInput.worldPos = input.worldPos;
    surfaceInput.normalWS = n;
    surfaceInput.tangentWS = float4(t, input.tangent.w);
    surfaceInput.uv = input.uv;
    surfaceInput.baseColorFactor = inst.baseColor;
    surfaceInput.metallicRoughnessAO = inst.metallicRoughnessAO;
    surfaceInput.emissiveFactor = inst.emissive;
    surfaceInput.alphaCutoff = inst.params_.x;

    T surface = T();
    float4 baseColor = surface.GetBaseColor(surfaceInput);
    float3 mrAO = surface.GetMetallicRoughnessAO(surfaceInput);

    // Alpha test: discard fragments below the cutoff (0 disables the test).
    if (surfaceInput.alphaCutoff > 0.0 && baseColor.a < surfaceInput.alphaCutoff)
    {
        discard;
    }

    albedoRT = float4(EncodeSRGB(baseColor.rgb), mrAO.y);

    float3 normalTS = surface.GetNormalTS(surfaceInput);
    float3 worldNormal = normalize(t * normalTS.x + b * normalTS.y + n * normalTS.z);
    float2 geometryNormal = EncodeGeometryNormal(n);
    normalRT = float4(worldNormal * 0.5 + 0.5, geometryNormal.x);

    mrAORT = float4(mrAO, 1.0);

    // Stored linear in the RGBA16Float target; no shading applied downstream.
    emissiveRT = float4(surface.GetEmissive(surfaceInput), geometryNormal.y);
}
