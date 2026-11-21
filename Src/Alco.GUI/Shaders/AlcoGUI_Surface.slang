#language slang 2025
module AlcoGUI_Surface;

import Core;
import SharedSampler;

public static const uint UIQuadStraight = 0u;
public static const uint UIQuadPremultiplied = 1u;
public static const uint UIQuadDistanceGlyph = 2u;
public static const uint UIQuadBitmapGlyph = 3u;
public static const uint UIQuadStraightTiled = 4u;
public static const uint UIQuadPremultipliedTiled = 5u;
public static const uint UIQuadStraightNearest = 6u;
public static const uint UIQuadPremultipliedNearest = 7u;

public struct UIInstance
{
    public float2 Origin;
    public float2 Right;
    public float2 Down;
    public uint TextureIndex;
    public uint Kind;
    public float4 UvRect;
    public float4 Color;
};

public struct UIPassHeader
{
    public GPUBufferView Quads;
    public float2 ClipScale;
};

public struct UIVsOut
{
    public float4 Position : SV_Position;
    public float2 Uv : TEXCOORD0;
    public float4 Color : COLOR0;
    public nointerpolation uint TextureIndex : TEXCOORD1;
    public nointerpolation uint Kind : TEXCOORD2;
    public float2 LocalUv : TEXCOORD3;
};

public struct UISurfaceInput
{
    public float2 Uv;
    public float2 LocalUv;
    public float4 Color;
    public float4 BaseColor;
    public uint TextureIndex;
    public uint Kind;
};

public interface IUISurface
{
    associatedtype Params;
    float4 Shade(UISurfaceInput input, Params p);
}

public UIVsOut uiExpandQuadVertex(UIInstance inst, float2 clipScale, uint vertexId)
{
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
    output.Position = float4(worldPos * clipScale, 0.0, 1.0);
    output.Uv = inst.UvRect.xy + inst.UvRect.zw * local;
    output.Color = inst.Color;
    output.TextureIndex = inst.TextureIndex;
    output.Kind = inst.Kind;
    output.LocalUv = local;
    return output;
}

public float4 uiSampleTexture(uint textureIndex, uint kind, float2 uv)
{
    Texture2D<float4> tex = gpu_texture<Texture2D<float4>>(gpu_nonuniform_index(textureIndex));
    if (kind == UIQuadStraightTiled || kind == UIQuadPremultipliedTiled)
        return tex.SampleLevel(shared_sampler_linear_repeat(), uv, 0.0);
    if (kind == UIQuadStraightNearest || kind == UIQuadPremultipliedNearest)
        return tex.SampleLevel(shared_sampler_nearest_clamp(), uv, 0.0);
    return tex.SampleLevel(shared_sampler_linear_clamp(), uv, 0.0);
}

public float4 uiShade(UIVsOut input)
{
    float4 texel = uiSampleTexture(input.TextureIndex, input.Kind, input.Uv);
    // Compute derivatives before any control flow on Kind so quad helper lanes stay valid.
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
