// file "AlcoParticles_Surface.slang" - module "AlcoParticles_Surface"
//
// The surface contract between particle material modules (user-owned code,
// referenced by .amat material assets) and the particle pass templates
// (GpuParticle2D/3D). Surfaces define an associated Params struct and evaluate
// per-fragment color / optional per-vertex world position.

#language slang 2025
module AlcoParticles_Surface;

import Core;

/// Per-fragment inputs evaluated by a particle surface.
public struct ParticleSurfaceInput
{
    public float2 uv;
    public float4 color;
    public float age01;
    public float3 worldPosition;
    public GpuTextureId spriteTexture;
};

/// Per-vertex inputs evaluated by a particle surface vertex hook.
public struct ParticleVertexInput
{
    public float3 world;
    public float3 particlePosition;
    public float age01;
    public float4x4 emitterWorldMatrix;
    public float customData;
    public float height;
};

/// Empty parameter block placeholder for parameterless particle surfaces.
public struct EmptyParticleParams
{
    public uint _pad;
};

/// Particle shading contract shared between 2D and 3D particle passes.
public interface IParticleSurface
{
    associatedtype Params;

    float4 getColor(ParticleSurfaceInput input, Params p)
    {
        return input.color;
    }

    float3 getWorldPosition(ParticleVertexInput input, Params p)
    {
        return input.world;
    }
}
