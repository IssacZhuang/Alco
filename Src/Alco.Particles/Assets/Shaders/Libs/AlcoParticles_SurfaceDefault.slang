// file "AlcoParticles_SurfaceDefault.slang" - module "AlcoParticles_SurfaceDefault"
//
// The default particle surface: the "texture" slot (the particle sprite)
// multiplied by the particle color - the visual of every group whose material
// names no surface module of its own. The particle systems compose it as the
// material compiler's default surface (ParticleAssetPipeline.DefaultSurface).

#language slang 2025
module AlcoParticles_SurfaceDefault;

import AlcoRendering_Core; // samplers
import AlcoParticles_Surface;

public struct MaterialParamsData
{
    public Texture2D texture;
};

public ParameterBlock<MaterialParamsData> material;

public struct Surface : IParticleSurface
{
    public override float4 shade(ParticleSurfaceInput input)
    {
        return material.texture.Sample(samplers.linearClamp, input.uv) * input.color;
    }
}
