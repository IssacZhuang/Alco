#ifndef SURFACE_HLSLI
#define SURFACE_HLSLI

// The contract between pass templates (GBuffer.hlsl, ShadowDepth.hlsl, ...)
// and surface shaders (material-owned code). A pass template owns the shader
// entry points, the render-target layout and all pass-mandated state; it calls
// only the functions below whose outputs it consumes. Whatever a pass leaves
// uncalled — functions, textures, samplers — is dead code the compiler strips
// from that pass's permutation, structurally: surfaces never branch on pass
// defines and adding a pass touches no surface. Shadow depth calls
// GetBaseColor for its alpha; the RSM adds GetNormalTS; the G-buffer consumes
// everything.
//
// A surface declares its resources next to the functions, ALL in binding
// set 2: the engine requires set indices contiguous from 0, the pass templates
// pack their own resources into sets 0-1, and one shared set keeps the layout
// dense no matter which subset of functions a pass consumes (multiple
// resources may share a set by being declared one after another — see DEFINE_*
// in Core.hlsli; binding is by resource name).
//
// The function grouping follows resource granularity, not field granularity:
// base color carries albedo and alpha (one texture, one fetch), metallic,
// roughness and AO share one fetch. A surface with expensive intermediates
// shared across functions (e.g. triplanar weights) recomputes them per
// function — keep such surfaces honest about what each call needs, or fold
// the work into fewer calls.
//
// Surface parameters: a surface may declare a uniform block named
// _materialParams whose members are all bare float4s (one 16-byte register
// each — the packing the material compiler assumes). A material asset fills
// the registers by member name through its "parameters" object; members the
// asset leaves out read zero.
//
// Time is not part of this contract: surfaces that need it declare the
// engine's _globalRenderData cbuffer —
//   DEFINE_UNIFORM(2, _globalRenderData) { float4 time; }
// with x = time, y = deltaTime, z = sinTime, w = cosTime — and read it
// directly, in ModifyVertex (vertex stage) or any Get* alike. The engine
// binds the per-frame GlobalRenderDataBuffer to every material it creates
// (see RenderingSystem.CreateMaterial), so no template or pass wiring
// is involved.
//
// A surface file may internally #include whatever it needs (shared noise
// libraries, aspect implementations) — file organization is the surface's
// own; passes only ever see the one entry file the material composer swaps
// into the template's @SURFACE@ line.

/// Interpolated geometry and per-instance factors available to a surface.
struct SurfaceInput
{
    float3 worldPos;            // interpolated world-space position
    float3 normalWS;            // normalized mesh normal, world space
    float4 tangentWS;           // re-orthogonalized tangent (xyz) + bitangent sign (w)
    float2 uv;                  // the mesh UV
    float4 baseColorFactor;     // linear tint (rgb) + alpha multiplier (a)
    float4 metallicRoughnessAO; // metallic (x), roughness (y), ambient occlusion (z)
    float4 emissiveFactor;      // linear emissive factor (rgb)
    float alphaCutoff;          // alpha test threshold; 0 disables the test
};

/// Base color: linear albedo (rgb) and coverage alpha (a) — one fetch.
float4 GetBaseColor(SurfaceInput input);

/// Tangent-space normal; lifted to world space by the pass template's TBN.
float3 GetNormalTS(SurfaceInput input);

/// PBR factors: metallic (x), roughness (y), ambient occlusion (z) — one fetch.
float3 GetMetallicRoughnessAO(SurfaceInput input);

/// Linear emissive radiance.
float3 GetEmissive(SurfaceInput input);

/// Vertex deformation in world space, applied after the instance transform and
/// before projection in every pass. Implemented by every surface shader;
/// identity in PbrStandard. Time, when needed, comes from _globalRenderData.
void ModifyVertex(inout float3 worldPos, inout float3 normalWS, float2 uv);

#endif
