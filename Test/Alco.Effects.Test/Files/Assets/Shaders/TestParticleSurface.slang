// Test-only particle surface (the fixture of the .amat material path): a
// dissolve surface exercising the shared pieces of particle materials - a
// [MaterialParams] block mixing uniform parameters with texture slots (the
// noise map is the "shared texture" a material asset carries) and the
// conventional "texture" slot a group overrides with its own sprite. It also
// overrides the 2D vertex hook as a pass-through so the validation suite
// covers that linkage path too.

#language slang 2025
module TestParticleSurface;

import AlcoRendering_Core; // samplers, [MaterialParams]
import AlcoParticles_Surface; // IParticleSurface, ParticleSurfaceInput, ParticleVertexInput

public struct DissolveParamsData
{
    public float edgeWidth;
    public float3 edgeColor;
    public float noiseScale;

    public Texture2D noiseTexture;
    public Texture2D texture;
};

[MaterialParams]
public ParameterBlock<DissolveParamsData> dissolveParams;

public struct Surface : IParticleSurface
{
    public override float3 getWorldPosition(ParticleVertexInput input)
    {
        // Pass-through that still reads the input, so the composed vertex
        // shader provably links the override.
        return input.world + input.particlePosition * 0.0;
    }

    public override float4 getColor(ParticleSurfaceInput input)
    {
        float4 texel = dissolveParams.texture.Sample(samplers.linearClamp, input.uv);
        float noise = dissolveParams.noiseTexture.Sample(
            samplers.linearRepeat, input.uv * dissolveParams.noiseScale).r;
        float cut = 1.0 - input.age01;
        float alive = noise <= cut ? 1.0 : 0.0;
        float edge = smoothstep(cut - dissolveParams.edgeWidth, cut, noise) * alive;
        float4 color = texel * input.color;
        color.rgb += dissolveParams.edgeColor * edge;
        color.a *= alive;
        return color;
    }
}
