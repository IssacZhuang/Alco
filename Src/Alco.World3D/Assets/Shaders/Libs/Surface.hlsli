#ifndef SURFACE_HLSLI
#define SURFACE_HLSLI

// The contract between pass templates (GBuffer.hlsl, ShadowDepth.hlsl, ...)
// and surface shaders (material-owned code). A pass template owns the shader
// entry points, the render-target layout and all pass-mandated state; it calls
// the two functions declared here to evaluate the material. A surface shader
// implements them and declares its own resources next to them, from binding
// set 2 upward: the engine requires set indices contiguous from 0, and the
// pass templates pack their own resources (camera, instances, cascade data,
// lights, shadow map) into sets 0-1, so a surface's declared sets must continue
// contiguously from 2 (PbrStandard uses 2-5; a surface declaring nothing — a
// fully procedural material — leaves the templates' sets 0-1 dense on their
// own). Multiple resources may share one set by being declared one after
// another (see DEFINE_* in Core.hlsli); binding is by resource name.
//
// Surface parameters: a surface may declare a uniform block named
// _materialParams whose members are all bare float4s (one 16-byte register
// each — the packing the material compiler assumes). A material asset fills
// the registers by member name through its "parameters" object; members the
// asset leaves out read zero.
//
// The same surface functions run in every pass that draws the mesh (G-buffer,
// shadow depth, RSM, glass), so vertex animation applied in ModifyVertex stays
// consistent across passes automatically. Gate expensive fragment work on the
// pass define (PASS_GBUFFER / PASS_SHADOW / ...) when a pass only needs part of
// the output — shadow depth, for example, only needs alpha.

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
    float time;                 // seconds since startup (0 until a global time buffer is wired)
};

/// The material evaluated at one point. albedo/emissive are linear; normalTS is
/// tangent-space and lifted to world space by the pass template's TBN.
struct SurfaceOutput
{
    float3 albedo;
    float alpha;
    float3 normalTS;
    float roughness;
    float metallic;
    float ao;
    float3 emissive;
};

/// Evaluate the material at one point. Implemented by every surface shader.
SurfaceOutput EvaluateSurface(SurfaceInput input);

/// Vertex deformation in world space, applied after the instance transform and
/// before projection in every pass. Implemented by every surface shader;
/// identity in PbrStandard.
void ModifyVertex(inout float3 worldPos, inout float3 normalWS, float2 uv, float time);

#endif // SURFACE_HLSLI
