// file "AlcoRendering_UIStandard.slang" - module "AlcoRendering_UIStandard"
//
// Default UI sprite surface: sample the "texture" slot and apply the draw tint.
// The template's nearest axis controls filtering. Preserve the source's alpha
// representation: ordinary textures are straight RGBA, while premultiplied SVG
// textures retain their existing output for the caller's matching blend state.

#language slang 2025
module AlcoRendering_UIStandard;

import AlcoRendering_UI;

public struct MaterialParamsData
{
    public Texture2D texture;
};

public ParameterBlock<MaterialParamsData> material;

public struct Surface : IUISurface
{
    public float4 getUIColor(UISurfaceInput input)
    {
        return material.texture.Sample(input.mainSampler, input.uv) * input.tint;
    }
}
