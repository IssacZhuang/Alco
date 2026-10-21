// file "AlcoRendering_UIText.slang" - module "AlcoRendering_UIText"
//
// UI text pass over IUISurface. Preserve the engine Text glyph-buffer and
// push-constant ABI, the font texture binding, and per-character rich-text tint.
// Surface UVs stay glyph-local; atlas UVs are used only for font coverage.
// The surface supplies straight RGBA. Coverage modulates its alpha, then RGB
// is premultiplied by that final alpha for PremultipliedAlpha blending.

#language slang 2025
module AlcoRendering_UIText;

import AlcoRendering_Core;
import AlcoRendering_UI;

struct Vertex
{
    float3 position : POSITION;
    float2 uv : TEXCOORD0;
    uint instanceID : SV_VulkanInstanceID;
};

struct V2F
{
    float4 position : SV_POSITION;
    float2 uv : TEXCOORD0;
    float3 drawPos : TEXCOORD1;
    nointerpolation uint instanceID : TEXCOORD2;
};

public struct Constants
{
    float4x4 model;
    float2 vertexOffset; // offset the text to its pivot point
};

public struct TextData
{
    float4 uvRect;
    float4 color;
    float2 offset;
    float2 size;
};

public struct CameraParams
{
    public float4x4 viewProjection;
};

public ParameterBlock<CameraParams> camera;

public struct TextBufferParams
{
    public RWStructuredBuffer<TextData> textBuffer;
};

public ParameterBlock<TextBufferParams> textBuffer;

public struct MaterialParamsData
{
    public Texture2D font;
};

public ParameterBlock<MaterialParamsData> material;

[[vk::push_constant]] Constants constants;

[shader("vertex")]
V2F MainVS<T : IUISurface>(Vertex input)
{
    TextData data = textBuffer.textBuffer[input.instanceID];
    float3 vertexPos = input.position * float3(data.size, 1.0f);
    float3 offset = float3(data.offset + constants.vertexOffset, 0.0f);
    float4 position = float4(vertexPos + offset, 1.0f);
    position = mul(constants.model, position);

    V2F output = (V2F)0;
    output.position = mul(camera.viewProjection, position);
    output.uv = input.uv;
    output.drawPos = position.xyz;
    output.instanceID = input.instanceID;
    return output;
}

[shader("fragment")]
float4 MainPS<T : IUISurface, let nearest : bool>(V2F input) : SV_TARGET
{
    TextData data = textBuffer.textBuffer[input.instanceID];
    float2 uv = input.uv * data.uvRect.zw + data.uvRect.xy;
    float coverage = material.font.Sample(samplers.linearRepeat, uv).r;

    T surface = T();
    UISurfaceInput surfaceInput;
    surfaceInput.tint = data.color;
    surfaceInput.uv = input.uv;
    surfaceInput.drawPos = input.drawPos;
    surfaceInput.mainSampler = uiMainSampler<nearest>();
    float4 color = surface.getUIColor(surfaceInput);

    float alpha = coverage * color.a;
    return float4(color.rgb * alpha, alpha);
}
