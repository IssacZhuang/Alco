// file "surface.slang" - module "surface"
//
// The Slang surface contract between pass templates (gbuffer.slang-template,
// shadow_depth.slang-template, rsm.slang-template) and surface modules
// (material-owned code). The Slang-native replacement of Shaders/Libs/Surface.hlsli:
// the contract is an interface, so the material compiler type-checks every
// surface against it at compile time instead of relying on textual conventions.
//
// A surface module must export `public struct Surface : ISurface`. Its shader
// resources (textures, samplers, uniform blocks) are declared at the surface
// module's global scope; the engine binds them by name, exactly like the HLSL
// surface contract. A surface may declare a `_materialParams` cbuffer with
// members of ANY scalar/vector type (unlike the HLSL float4-only convention) -
// the material compiler reads each member's type and byte offset from Slang
// reflection and fills the buffer accordingly.
//
// Surfaces never branch on pass defines; whatever a pass leaves uncalled is
// up to the compiler's dead-code elimination. Time, when needed, comes from
// the engine-bound `_globalRenderData` cbuffer declared at the surface's
// global scope.

public struct SurfaceInput
{
    public float3 worldPos;            // interpolated world-space position
    public float3 normalWS;            // normalized mesh normal, world space
    public float4 tangentWS;           // re-orthogonalized tangent (xyz) + bitangent sign (w)
    public float2 uv;                  // the mesh UV
    public float4 baseColorFactor;     // linear tint (rgb) + alpha multiplier (a)
    public float4 metallicRoughnessAO; // metallic (x), roughness (y), ambient occlusion (z)
    public float4 emissiveFactor;      // linear emissive factor (rgb)
    public float alphaCutoff;          // alpha test threshold; 0 disables the test
}

public interface ISurface
{
    /// Base color: linear albedo (rgb) and coverage alpha (a) - one fetch.
    float4 GetBaseColor(SurfaceInput input);

    /// Tangent-space normal; lifted to world space by the pass template's TBN.
    float3 GetNormalTS(SurfaceInput input);

    /// PBR factors: metallic (x), roughness (y), ambient occlusion (z) - one fetch.
    float3 GetMetallicRoughnessAO(SurfaceInput input);

    /// Linear emissive radiance.
    float3 GetEmissive(SurfaceInput input);

    /// Vertex deformation in world space, applied after the instance transform
    /// and before projection in every pass. Identity in PbrStandard.
    void ModifyVertex(inout float3 worldPos, inout float3 normalWS, float2 uv);
}
