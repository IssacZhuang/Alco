#include "Shaders/Libs/Core.hlsli"

// Reflective shadow map (RSM) pass shader for the voxel GI's sun-bounce
// injection (CRYENGINE SVOTI style, see docs/GI_Sun_RSM_Injection.md). Renders
// the scene from the sun's point of view — the selected CSM cascade — into
// albedo + world-normal color targets, so the GI cone trace can sample
// shadow-map-resolution sun radiance where its march touches geometry.
//
// The pass reuses the shadow pass's per-cascade view-projection uniform (the
// matrices folded into the 2x2 atlas quadrants by RGNode_ShadowPass) and
// unfolds the quadrant back to full NDC here, so no separate matrix upload
// exists for this pass. The vertex layout must match Alco.Rendering.VertexPBR
// exactly.

struct Vertex
{
    float3 position : POSITION;
    float3 normal : NORMAL;
    float2 uv : TEXCOORD0;
    float4 tangent : TANGENT;
};

struct V2F
{
    float4 position : SV_POSITION;
    float3 normal : TEXCOORD0;
    float2 uv : TEXCOORD1;
};

struct Constants
{
    float4x4 model;
    float4 baseColor; // rgb = linear tint, a multiplies the albedo texture alpha
    // x = shadow cascade index (the RSM cascade)
    // y = alphaCutoff (0 disables alpha testing)
    // zw unused
    float4 params_;
};

// Per-cascade light view-projection matrices, updated per frame on the CPU and
// shared with ShadowDepth.hlsl (reference semantics keep render bundles valid
// while the camera-fitted cascades move).
DEFINE_UNIFORM(0, _data)
{
    float4x4 lightViewProjections[4];
};

DEFINE_TEX2D_SAMPLE(1, _albedoTexture);

PUSH_CONSTANT Constants constants;

// Linear RGB to sRGB encoding (the albedo target is RGBA8Unorm, matching the
// G-buffer's albedo path; the GI trace decodes it back).
float3 EncodeSRGB(float3 color)
{
    float3 lo = color * 12.92;
    float3 hi = 1.055 * pow(max(color, 0.0), 1.0 / 2.4) - 0.055;
    return lerp(hi, lo, step(color, float3(0.0031308, 0.0031308, 0.0031308)));
}

[shader("vertex")]
V2F MainVS(Vertex input)
{
    V2F output = (V2F)0;
    float4 worldPosition = mul(constants.model, float4(input.position, 1.0f));
    uint cascade = (uint)constants.params_.x;
    float4 folded = mul(lightViewProjections[cascade], worldPosition);
    // RGNode_ShadowPass folds the cascade into its atlas quadrant with
    // ndc' = ndc * 0.5 + offset, offset = ((cascade % 2) - 0.5, 0.5 - cascade / 2).
    // The RSM covers a full target, so unfold back to [-1, 1] here.
    float2 quadrantOffset = float2(
        (float)(cascade % 2u) - 0.5f,
        0.5f - (float)(cascade / 2u));
    output.position = float4((folded.xy - quadrantOffset) * 2.0f, folded.zw);
    output.normal = mul((float3x3)constants.model, input.normal);
    output.uv = input.uv;
    return output;
}

[shader("pixel")]
void MainPS(V2F input,
    out float4 albedoRT : SV_TARGET0,
    out float4 normalRT : SV_TARGET1)
{
    float4 albedo = SAMPLE_TEX2D(_albedoTexture, input.uv);

    // Alpha test (mirrors GBuffer.hlsl): cutout meshes keep correctly shaped
    // bounce light, not the alpha-quantized silhouette.
    float alphaCutoff = constants.params_.y;
    if (alphaCutoff > 0.0 && albedo.a * constants.baseColor.a < alphaCutoff)
    {
        discard;
    }

    albedoRT = float4(EncodeSRGB(albedo.rgb * constants.baseColor.rgb), 1.0);
    float3 worldNormal = normalize(input.normal);
    normalRT = float4(worldNormal * 0.5 + 0.5, 1.0);
}
