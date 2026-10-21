// file "AlcoRendering_UI.slang" - module "AlcoRendering_UI"
//
// The engine-owned contract between UI surfaces and the UISprite/UIText pass
// templates. A material module exports exactly one public conforming struct;
// the material compiler discovers that type and specializes the templates with
// it. A world/UI material can implement its world contract and IUISurface on
// the same struct, with separate entry methods sharing its sampling helpers.
// This contract has no game-specific instance data or world-surface dependency.
//
// Surfaces own their textures and parameters in module-scope ParameterBlocks.
// Texture fields bind by member name; [MaterialParams] blocks receive the
// material asset's parameter values, just as in the other surface families.
//
// Alpha contract:
// - UISprite returns getUIColor unchanged. The caller chooses the blend state
//   and must match it to the surface output. Ordinary surfaces return straight
//   RGBA for AlphaBlend; premultiplied sources such as SVG textures can retain
//   their existing output for PremultipliedAlpha. There is no automatic alpha
//   conversion in the sprite template or the standard surface.
// - UIText consumes straight surface RGBA, applies glyph coverage to alpha,
//   and premultiplies RGB by that final alpha for PremultipliedAlpha blending.

#language slang 2025
module AlcoRendering_UI;

import AlcoRendering_Core;

/// The extracted per-fragment inputs shared by the UI sprite and text passes.
public struct UISurfaceInput
{
    public float4 tint;     // sprite tint or per-character rich-text color
    public float2 uv;       // sprite uvRect applied; glyph-local UV for text
    public float3 drawPos;  // model-transformed position before camera projection
    public SamplerState mainSampler; // the template's nearest-axis sampler choice
};

/// Resolves the material's nearest specialization without changing wrap behavior.
public SamplerState uiMainSampler<let nearest : bool>()
{
    if (nearest)
    {
        return samplers.nearestRepeat;
    }
    return samplers.linearRepeat;
}

/// Fragment shading independent of the world/entity surface contracts.
public interface IUISurface
{
    /// Returns the surface color according to the pass alpha contract above.
    float4 getUIColor(UISurfaceInput input);
}
