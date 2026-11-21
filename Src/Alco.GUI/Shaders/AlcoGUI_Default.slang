#language slang 2025
module AlcoGUI_Default;

import Core;
import SharedSampler;

static const uint UIQuadPremultiplied = 1u;
static const uint UIQuadDistanceGlyph = 2u;
static const uint UIQuadBitmapGlyph = 3u;
static const uint UIQuadStraightTiled = 4u;
static const uint UIQuadPremultipliedTiled = 5u;
static const uint UIQuadStraightNearest = 6u;
static const uint UIQuadPremultipliedNearest = 7u;

struct UIInstance
{
    float2 Origin;
    float2 Right;
    float2 Down;
    uint TextureIndex;
    uint Kind;
    float4 UvRect;
    float4 Color;
};

[GPURoot]
public struct UIDefaultRoot
{
    public GPUBufferView Quads;
    public float2 ClipScale;
};

struct UIVsOut
{
    float4 Position : SV_Position;
    float2 Uv : TEXCOORD0;
    float4 Color : COLOR0;
    nointerpolation uint TextureIndex : TEXCOORD1;
    nointerpolation uint Kind : TEXCOORD2;
};

[shader("vertex")]
UIVsOut vsMain(uint vertexId : SV_VertexID, uint instanceId : SV_InstanceID)
{
    UIDefaultRoot root = gpu_load_root<UIDefaultRoot>();
    UIInstance inst = gpu_load<UIInstance>(root.Quads, instanceId);

    float2 local;
    switch (vertexId)
    {
    case 0u: local = float2(0.0, 0.0); break;
    case 1u: local = float2(1.0, 0.0); break;
    case 2u: local = float2(1.0, 1.0); break;
    case 3u: local = float2(0.0, 0.0); break;
    case 4u: local = float2(1.0, 1.0); break;
    default: local = float2(0.0, 1.0); break;
    }

    float2 worldPos = inst.Origin + inst.Right * local.x + inst.Down * local.y;

    UIVsOut output;
    output.Position = float4(worldPos * root.ClipScale, 0.0, 1.0);
    output.Uv = inst.UvRect.xy + inst.UvRect.zw * local;
    output.Color = inst.Color;
    output.TextureIndex = inst.TextureIndex;
    output.Kind = inst.Kind;
    return output;
}

float4 sampleUiTexture(uint textureIndex, uint kind, float2 uv)
{
    Texture2D<float4> tex = gpu_texture<Texture2D<float4>>(gpu_nonuniform_index(textureIndex));
    if (kind == UIQuadStraightTiled || kind == UIQuadPremultipliedTiled)
        return tex.SampleLevel(shared_sampler_linear_repeat(), uv, 0.0);
    if (kind == UIQuadStraightNearest || kind == UIQuadPremultipliedNearest)
        return tex.SampleLevel(shared_sampler_nearest_clamp(), uv, 0.0);
    return tex.SampleLevel(shared_sampler_linear_clamp(), uv, 0.0);
}

[shader("pixel")]
float4 psMain(UIVsOut input) : SV_Target0
{
    float4 texel = sampleUiTexture(input.TextureIndex, input.Kind, input.Uv);
    float width = max(fwidth(texel.r), 1.0 / 255.0);
    float sdfAlpha = smoothstep(0.5 - width, 0.5 + width, texel.r);

    if (input.Kind == UIQuadDistanceGlyph || input.Kind == UIQuadBitmapGlyph)
    {
        float cov = input.Kind == UIQuadDistanceGlyph ? sdfAlpha : texel.r;
        float a = input.Color.a * cov;
        return float4(input.Color.rgb * a, a);
    }

    if (input.Kind == UIQuadPremultiplied ||
        input.Kind == UIQuadPremultipliedTiled ||
        input.Kind == UIQuadPremultipliedNearest)
    {
        return float4(texel.rgb * (input.Color.rgb * input.Color.a), texel.a * input.Color.a);
    }

    float4 combined = texel * input.Color;
    return float4(combined.rgb * combined.a, combined.a);
}
