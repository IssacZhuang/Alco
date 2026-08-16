#include "Shaders/Libs/Core.hlsli"

// Point light shadow atlas depth-only pass shader. Renders scene geometry into
// one face of the atlas: the per-face view-projection matrices are pre-folded
// into their atlas sub-rectangles by the CPU (RGNode_PointLightShadow /
// PointLightShadowMath) and uploaded as a uniform array (reference semantics,
// render-bundle friendly); the push constant selects the matrix by global index
// (slot*6 + face). The vertex layout must match Alco.Rendering.VertexPBR
// exactly.
//
// Compile with SHADOW_CUTOUT defined to enable alpha testing (samples
// _albedoTexture, discards below the cutoff) so cutout meshes cast correctly
// shaped shadows.
//
// Compile with PLS_CLEAR_FACE defined to get the face-clear variant used by the
// node to reset one scissored face rect to the far plane (render-pass clears
// are not scissorable and would wipe the cached static faces): the vertex
// shader draws the full-screen mesh at the far plane, ignoring all inputs.

/// Total matrix count: RGNode_PointLightShadow.MaxSlots (16) * 6 faces.
static const uint PLS_MATRIX_COUNT = 96u;

#ifndef PLS_CLEAR_FACE

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
#ifdef SHADOW_CUTOUT
    float2 uv : TEXCOORD0;
#endif
};

struct Constants
{
    float4x4 model;
    // x = face matrix index (slot*6 + face)
    // y = alphaCutoff (cutout only, 0 disables the test)
    // z = baseColorAlpha (cutout only)
    float4 params_;
};

// Folded per-face view-projections, updated by the CPU when slot assignments or
// light data change. Kept in a uniform buffer (reference semantics) so recorded
// render bundles stay valid while the atlas slots move.
DEFINE_UNIFORM(0, _data)
{
    float4x4 faceViewProjections[PLS_MATRIX_COUNT];
};

#ifdef SHADOW_CUTOUT
DEFINE_TEX2D_SAMPLE(1, _albedoTexture);
#endif

PUSH_CONSTANT Constants constants;

[shader("vertex")]
V2F MainVS(Vertex input)
{
    V2F output = (V2F)0;
    float4 worldPosition = mul(constants.model, float4(input.position, 1.0f));
    output.position = mul(faceViewProjections[(uint)constants.params_.x], worldPosition);
#ifdef SHADOW_CUTOUT
    output.uv = input.uv;
#endif
    return output;
}

[shader("pixel")]
void MainPS(V2F input)
{
#ifdef SHADOW_CUTOUT
    float alphaCutoff = constants.params_.y;
    if (alphaCutoff > 0.0)
    {
        float alpha = SAMPLE_TEX2D(_albedoTexture, input.uv).a;
        clip(alpha * constants.params_.z - alphaCutoff);
    }
#endif
}

#else // PLS_CLEAR_FACE

// Face-clear variant: the full-screen mesh already carries NDC positions; place
// every fragment at the far plane (z = 1 in the 0..1 convention). Bound with an
// Always depth test and depth writes, drawn under the face's scissor rect.

struct ClearVertex
{
    float3 position : POSITION;
    float2 uv : TEXCOORD0;
};

struct ClearV2F
{
    float4 position : SV_POSITION;
};

DEFINE_UNIFORM(0, _data)
{
    float4x4 faceViewProjections[PLS_MATRIX_COUNT];
};

[shader("vertex")]
ClearV2F MainVS(ClearVertex input)
{
    ClearV2F output;
    output.position = float4(input.position.xy, 1.0f, 1.0f);
    return output;
}

[shader("pixel")]
void MainPS(ClearV2F input)
{
}

#endif // PLS_CLEAR_FACE
