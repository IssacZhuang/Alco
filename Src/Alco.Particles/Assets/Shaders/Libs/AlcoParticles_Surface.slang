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
// slang's component system: the template's generic fragment entry takes the
// surface type as its specialization argument (discovered from the surface
// module, exactly one conformer), so slang type-checks it against this
// contract at compile time.
//
// The contract is dimension-neutral: the same surface shades 2D and 3D
// particles. A group overrides the surface's "texture" slot (by convention the
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

/// The particle shading contract: the fragment color of one particle fragment.
/// The default shades a textureless particle as a flat colored quad. Slang
/// requires the `override` keyword when replacing this default implementation.
public interface IParticleSurface
{
    float4 shade(ParticleSurfaceInput input) { return input.color; }
}
