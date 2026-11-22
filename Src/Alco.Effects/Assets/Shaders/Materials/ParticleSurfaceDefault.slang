// file "ParticleSurfaceDefault.slang" - module "ParticleSurfaceDefault"
//
// The default particle surface: samples the emitter's sprite texture
// (delivered bindlessly via input.spriteTexture) multiplied by particle color.

#language slang 2025
module ParticleSurfaceDefault;

import Core;
import SharedSampler;
import AlcoParticles_Surface;

public struct ParticleDefaultParams
{
    public GpuTextureId texture;
};

public struct Surface : IParticleSurface
{
    public typedef ParticleDefaultParams Params;

    public override float4 getColor(ParticleSurfaceInput input, Params p)
    {
        return gpu_texture<Texture2D<float4>>(gpu_nonuniform_index(input.spriteTexture.index))
            .Sample(shared_sampler_linear_clamp(), input.uv) * input.color;
    }
}
