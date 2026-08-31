// file "SbDissolve2D.slang" - module "SbDissolve2D"
//
// Sandbox custom particle surface: a dissolve (erosion) effect — the particle
// burns away over its life, driven by the material's shared noise texture,
// with a glowing edge. Demonstrates the material-asset path of particle
// groups: the surface module, the shared noise texture and the uniform
// parameters all come from the .amat; the group derives only its own sprite
// over the surface's "texture" slot.

#language slang 2025
module SbDissolve2D;

import AlcoRendering_Core; // samplers, [MaterialParams]
import AlcoParticles_Surface; // IParticleSurface, ParticleSurfaceInput

public struct DissolveParamsData
{
    public float noiseScale;       // noise UV tiling over the particle quad
    public float edgeWidth;        // width of the glowing dissolve edge (noise units)
    public float3 edgeColor;       // edge glow color (linear)

    public Texture2D noiseTexture; // shared, bound by the .amat
    public Texture2D texture;      // the group's sprite (overridden per group)
};

[MaterialParams]
public ParameterBlock<DissolveParamsData> dissolveParams;

public struct Surface : IParticleSurface
{
    public override float4 shade(ParticleSurfaceInput input)
    {
        float4 texel = dissolveParams.texture.Sample(samplers.linearClamp, input.uv);
        float noise = dissolveParams.noiseTexture.Sample(
            samplers.linearRepeat, input.uv * dissolveParams.noiseScale).r;
        // The burn front sweeps from 1 (fully present) to 0 (fully dissolved)
        // over the particle's age.
        float cut = 1.0 - input.age01;
        if (noise > cut)
        {
            discard;
        }
        float edge = smoothstep(cut - dissolveParams.edgeWidth, cut, noise);
        float4 color = texel * input.color;
        color.rgb += dissolveParams.edgeColor * edge * 2.0;
        return color;
    }
}
