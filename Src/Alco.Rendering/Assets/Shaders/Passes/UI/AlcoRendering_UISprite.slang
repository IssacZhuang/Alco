// file "AlcoRendering_UISprite.slang" - module "AlcoRendering_UISprite"
//
// Non-instanced UI sprite pass over the engine-owned IUISurface contract.
// Keep the SpriteConstant push-constant ABI (model, color, uvRect), vertex
// inputs and camera block used by canvas/direct sprite draws. The surface
// owns texturing; its result is emitted unchanged, and the caller selects the
// matching blend state and depth/stencil state for clipping.

#language slang 2025
module AlcoRendering_UISprite;

import AlcoRendering_UI;

struct Vertex
{
    float3 position : TEXCOORD0;
    float2 uv : TEXCOORD1;
};

struct V2F
{
    float4 position : SV_POSITION;
    float2 uv : TEXCOORD0;
    float3 drawPos : TEXCOORD1;
};

struct Constants
{
    float4x4 model;
    float4 color;
    float4 uvRect;
};

[[vk::push_constant]] Constants constants;

public struct CameraParams
{
    public float4x4 viewProjection;
};

public ParameterBlock<CameraParams> camera;

[shader("vertex")]
V2F MainVS<T : IUISurface>(Vertex input)
{
    V2F output;
    float4 drawPos = mul(constants.model, float4(input.position, 1.0f));
    output.position = mul(camera.viewProjection, drawPos);
    output.uv = input.uv * constants.uvRect.zw + constants.uvRect.xy;
    output.drawPos = drawPos.xyz;
    return output;
}

[shader("fragment")]
float4 MainPS<T : IUISurface, let nearest : bool>(V2F input) : SV_TARGET
{
    T surface = T();
    UISurfaceInput surfaceInput;
    surfaceInput.tint = constants.color;
    surfaceInput.uv = input.uv;
    surfaceInput.drawPos = input.drawPos;
    surfaceInput.mainSampler = uiMainSampler<nearest>();
    return surface.getUIColor(surfaceInput);
}
