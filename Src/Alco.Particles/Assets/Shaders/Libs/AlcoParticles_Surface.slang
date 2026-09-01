// file "AlcoParticles_Surface.slang" - module "AlcoParticles_Surface"
//
// The surface contract between particle material modules (user-owned code,
// referenced by .amat material assets) and the particle pass templates
// (GpuParticle2D/3D). A particle material module exports exactly one public
// struct implementing IParticleSurface - the type name is free - and declares
// its textures and uniform parameters at module scope: texture slots are the
// fields of its ParameterBlocks (bound from the material asset's texture table
// by field name), uniform parameter blocks are marked [MaterialParams] (filled
// from the asset's parameter table) - the shared conventions of
// AlcoWorld3D_Surface, which this file mirrors.
//
// The material compiler composes a pass template with a surface module through
// slang's component system: the template's generic entry points take the
// surface type as their specialization argument (discovered from the surface
// module, exactly one conformer), so slang type-checks it against this
// contract at compile time.
//
// The shading contract is dimension-neutral: the same surface shades 2D and 3D
// particles, and the vertex-stage hook (adjustWorldPosition) takes the
// dimension-neutral ParticleVertexInput — each pass template fills it from its
// own particle/emitter records, so one surface module serves both dimensions.
// A group overrides the surface's "texture" slot (by convention the
// particle sprite) with its own texture - material-instance derivation.

#language slang 2025
module AlcoParticles_Surface;

/// The per-fragment inputs a particle surface shades from.
public struct ParticleSurfaceInput
{
    public float2 uv;     // the quad UV with the flipbook frame applied
    public float4 color;  // the particle's color over life x the group's tint
    public float age01;   // the normalized particle age in [0, 1]
};

/// The per-vertex inputs a particle surface's vertex hook adjusts from. Both
/// pass templates fill this from their own particle/emitter records; fields a
/// hook never reads are dead-code-eliminated. Adding fields later is backward
/// compatible — templates fill them, existing surfaces ignore them.
public struct ParticleVertexInput
{
    public float3 world;            // the quad corner's world position (post model transform, pre projection)
    public float3 particlePosition; // the particle's world position (the corner = center + quad offset)
    public float age01;             // the normalized particle age in [0, 1]
    public float4x4 emitterWorldMatrix; // the emitter's spawn-time world transform (CPU-written every frame)
    public float customData;        // the instance's custom data channel (EmitterParams.CustomData, 0 unless set)
    public float height;            // projected 2D height; zero for 3D particles
};

/// The particle shading contract: the fragment color of one particle fragment,
/// plus an optional vertex-stage hook. Both methods carry defaults — a surface
/// that only colors fragments overrides shade alone. Slang requires the
/// `override` keyword when replacing a default implementation.
public interface IParticleSurface
{
    float4 shade(ParticleSurfaceInput input) { return input.color; }

    /// Adjusts a quad corner's world position before the projection (default:
    /// unchanged). Override only when the material moves vertices (e.g. facade
    /// depth writes).
    float3 adjustWorldPosition(ParticleVertexInput input) { return input.world; }
}
