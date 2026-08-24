// file "alco-world3d-surface.slang" - module "alco_world3d_surface"
//
// The surface contract between material modules (user-owned code) and pass
// templates (rendering facilities: G-buffer, shadow depth, RSM, forward glass,
// voxel GI). A material module exports `public struct Surface : ISurface` and
// overrides ONLY the attributes it customizes - every interface method has a
// default implementation, the equivalent of an unconnected material pin. Slang
// requires the `override` keyword when replacing a defaulted interface method.
//
// The material compiler composes a pass template with a surface module through
// slang's component system (composite + link-time specialization, no generated
// code): the template's generic entry points ([shader] MainVS/MainPS/MainCS)
// take the surface type as their first specialization argument, so slang
// type-checks every surface against the contract at compile time.
//
// Built-in pass consumption:
//   gbuffer      : IVertexSurface + IAlbedoSurface + INormalSurface +
//                  IMaterialPropsSurface + IEmissiveSurface
//   shadow-depth : IVertexSurface (+ IAlbedoSurface when AlphaTest specializes on)
//   rsm          : IVertexSurface + IAlbedoSurface + INormalSurface
//   glass        : IVertexSurface + IAlbedoSurface + INormalSurface +
//                  IMaterialPropsSurface + IEmissiveSurface
//   voxelize     : IVoxelFeedSurface (compute - see the LOD rule below)
//
// Whatever a pass leaves uncalled is dead code after specialization.
//
// Surface resources (textures, samplers, uniform blocks) are declared at the
// surface module's global scope in set-scoped blocks (a cbuffer that declares
// only its set, e.g. `cbuffer _material` in space2); the engine binds them by
// name. A surface marks its material-parameter cbuffers with [MaterialParams]
// (declared in alco_rendering_core — import it) — any names, any number of
// blocks, members mixing ANY scalar/vector float types; the engine discovers
// the marked blocks from Slang reflection, reads each member's type and byte
// offset, and fills the buffers from the material asset's parameter table.
// Unmarked blocks (engine data such as _globalRenderData) are never treated
// as material parameters.
//
// Fragment-domain methods (GetBaseColor & co.) run in vertex/fragment stages
// and may sample with implicit derivatives (Sample). The voxel-GI feed runs in
// a COMPUTE stage where implicit derivatives do not exist: GetVoxelBaseColor /
// GetVoxelEmissive receive an explicit LOD and must sample with SampleLevel.
//
// Time, when needed, comes from the engine-bound `_globalRenderData` cbuffer
// declared at the surface's global scope.

#language slang 2025
module alco_world3d_surface;

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

/// Vertex deformation, applied after the instance transform and before
/// projection in every pass, so silhouettes stay consistent across passes.
public interface IVertexSurface
{
    void ModifyVertex(inout float3 worldPos, inout float3 normalWS, float2 uv) { }
}

/// Base color: linear albedo (rgb) and coverage alpha (a) - one fetch.
public interface IAlbedoSurface
{
    float4 GetBaseColor(SurfaceInput input) { return input.baseColorFactor; }
}

/// Tangent-space normal; lifted to world space by the pass template's TBN.
public interface INormalSurface
{
    float3 GetNormalTS(SurfaceInput input) { return float3(0, 0, 1); }
}

/// PBR factors: metallic (x), roughness (y), ambient occlusion (z).
public interface IMaterialPropsSurface
{
    float3 GetMetallicRoughnessAO(SurfaceInput input) { return input.metallicRoughnessAO.xyz; }
}

/// Linear emissive radiance.
public interface IEmissiveSurface
{
    float3 GetEmissive(SurfaceInput input) { return input.emissiveFactor.rgb; }
}

/// The voxel-GI feed: what the compute voxelizer accumulates per voxel. Runs in
/// a compute stage - implementations MUST sample textures with SampleLevel and
/// the passed LOD (implicit derivatives do not exist outside fragment). The
/// defaults keep a textureless material's GI contribution factor-exact.
public interface IVoxelFeedSurface
{
    float4 GetVoxelBaseColor(SurfaceInput input, float lod) { return input.baseColorFactor; }
    float3 GetVoxelEmissive(SurfaceInput input, float lod) { return input.emissiveFactor.rgb; }
}

/// The full material surface: every built-in pass consumes a subset. Implement
/// it with an empty struct and override only what the material customizes:
///     public struct Surface : ISurface
///     {
///         public override float3 GetEmissive(SurfaceInput input) { ... }
///     }
public interface ISurface : IVertexSurface, IAlbedoSurface, INormalSurface,
    IMaterialPropsSurface, IEmissiveSurface, IVoxelFeedSurface { }
