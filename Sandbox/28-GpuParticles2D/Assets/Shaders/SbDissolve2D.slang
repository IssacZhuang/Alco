// file "SbDissolve2D.slang" - module "SbDissolve2D"
//
// Sandbox custom particle surface: a dissolve (erosion) effect — the particle
// burns away over its life, driven by the material's shared noise texture,
// with a glowing edge. Demonstrates the material-asset path of particle
// groups: the surface module, the shared noise texture and the uniform
// parameters all come from the .amat; the group supplies its own bindless sprite.

#language slang 2025
module SbDissolve2D;

import Core;
import SharedSampler;
import AlcoParticles_Surface;

public struct DissolveParamsData
{
    public float noiseScale;       // noise UV tiling over the particle quad
    public float edgeWidth;        // width of the glowing dissolve edge (noise units)
    public float3 edgeColor;       // edge glow color (linear)
    public GpuTextureId noiseTexture; // shared, bound by the .amat
    public GpuTextureId texture;      // optional surface texture slot
};

public struct Surface : IParticleSurface
{
    public typedef DissolveParamsData Params;

    public override float4 getColor(ParticleSurfaceInput input, Params p)
    {
        float4 texel = gpu_texture<Texture2D<float4>>(gpu_nonuniform_index(input.spriteTexture.index))
            .Sample(shared_sampler_linear_clamp(), input.uv);
        float noise = gpu_texture<Texture2D<float4>>(gpu_nonuniform_index(p.noiseTexture.index))
            .Sample(shared_sampler_linear_repeat(), input.uv * p.noiseScale).r;
        // The burn front sweeps from 1 (fully present) to 0 (fully dissolved)
        // over the particle's age.
        float cut = 1.0 - input.age01;
        if (noise > cut)
        {
            discard;
        }
        float edge = smoothstep(cut - p.edgeWidth, cut, noise);
        float4 color = texel * input.color;
        color.rgb += p.edgeColor * edge * 2.0;
        return color;
    }
}
