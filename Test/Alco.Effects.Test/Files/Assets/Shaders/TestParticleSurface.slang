// Test-only particle surface (the fixture of the .amat material path): a
// dissolve surface exercising shared material parameters and texture slots plus
// per-emitter bindless spriteTexture, and the optional vertex hook.

#language slang 2025
module TestParticleSurface;

import Core;
import SharedSampler;
import AlcoParticles_Surface;

public struct DissolveParamsData
{
    public float edgeWidth;
    public float3 edgeColor;
    public float noiseScale;
    public GPUTextureId noiseTexture;
    public GPUTextureId texture;
};

public struct Surface : IParticleSurface
{
    public typedef DissolveParamsData Params;

    public override float3 getWorldPosition(ParticleVertexInput input, Params p)
    {
        return input.world + input.particlePosition * 0.0;
    }

    public override float4 getColor(ParticleSurfaceInput input, Params p)
    {
        float4 texel = gpu_texture<Texture2D<float4>>(gpu_nonuniform_index(input.spriteTexture.index))
            .Sample(shared_sampler_linear_clamp(), input.uv);
        float noise = gpu_texture<Texture2D<float4>>(gpu_nonuniform_index(p.noiseTexture.index))
            .Sample(shared_sampler_linear_repeat(), input.uv * p.noiseScale).r;
        float cut = 1.0 - input.age01;
        float alive = noise <= cut ? 1.0 : 0.0;
        float edge = smoothstep(cut - p.edgeWidth, cut, noise) * alive;
        float4 color = texel * input.color;
        color.rgb += p.edgeColor * edge;
        color.a *= alive;
        return color;
    }
}
