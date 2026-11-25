#language slang 2025
module AlcoRendering_Sprite2D;

import Core;
import SharedSampler;

public static const uint SPRITE2D_SAMPLER_MASK = 3u;
public static const uint SPRITE2D_SAMPLER_LINEAR_CLAMP = 0u;
public static const uint SPRITE2D_SAMPLER_LINEAR_REPEAT = 1u;
public static const uint SPRITE2D_SAMPLER_NEAREST_CLAMP = 2u;
public static const uint SPRITE2D_SAMPLER_NEAREST_REPEAT = 3u;

public static const uint SPRITE2D_KIND_SHIFT = 2u;
public static const uint SPRITE2D_KIND_MASK = 7u;
public static const uint SPRITE2D_KIND_STRAIGHT = 0u;
public static const uint SPRITE2D_KIND_PREMULTIPLIED = 1u;
public static const uint SPRITE2D_KIND_DISTANCE_GLYPH = 2u;
public static const uint SPRITE2D_KIND_BITMAP_GLYPH = 3u;

public struct Sprite2DQuad
{
    public float2 Origin;
    public float2 Right;
    public float2 Down;
    public float Depth;
    public uint TextureIndex;
    public float4 UvRect;
    public float4 Color;
    public uint Flags;
    public uint Pad0;
    public uint Pad1;
    public uint Pad2;
};

public struct Sprite2DPassHeader
{
    public float4x4 ViewProjection;
    public GPUBufferView Quads;
    public float Time;
    public uint Pad0;
};

public struct EmptySprite2DParams
{
    public uint _pad;
};

public struct Sprite2DVsOut
{
    public float4 Position : SV_Position;
    public float2 Uv : TEXCOORD0;
    public float4 Color : COLOR0;
    public nointerpolation uint TextureIndex : TEXCOORD1;
    public nointerpolation uint Flags : TEXCOORD2;
    public float2 LocalUv : TEXCOORD3;
    public float3 WorldPos : TEXCOORD4;
};

public struct Sprite2DSurfaceInput
{
    public float2 Uv;
    public float2 LocalUv;
    public float3 WorldPos;
    public float4 Color;
    public float4 BaseColor;
    public uint TextureIndex;
    public uint Flags;
    public float Time;
};

public interface ISprite2DSurface
{
    associatedtype Params;
    float4 Shade(Sprite2DSurfaceInput input, Params p);
};

public Sprite2DVsOut sprite2DExpandQuadVertex(Sprite2DQuad quad, float4x4 viewProjection, uint vertexId)
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

    float3 worldPos = float3(quad.Origin + quad.Right * local.x + quad.Down * local.y, quad.Depth);

    Sprite2DVsOut output;
    output.Position = mul(float4(worldPos, 1.0), viewProjection);
    output.Uv = quad.UvRect.xy + quad.UvRect.zw * local;
    output.Color = quad.Color;
    output.TextureIndex = quad.TextureIndex;
    output.Flags = quad.Flags;
    output.LocalUv = local;
    output.WorldPos = worldPos;
    return output;
}

public float4 sprite2DSampleTexture(uint textureIndex, uint flags, float2 uv)
{
    // Compute derivatives outside divergent sampler branches so VulkanCompact stays compliant
    // without shaderSamplerArrayNonUniformIndexing and preserves quad helper lane derivatives.
    float2 dx = ddx(uv);
    float2 dy = ddy(uv);
    Texture2D<float4> tex = gpu_texture<Texture2D<float4>>(gpu_nonuniform_index(textureIndex));
    uint samplerMode = flags & SPRITE2D_SAMPLER_MASK;
    if (samplerMode == SPRITE2D_SAMPLER_LINEAR_REPEAT)
        return tex.SampleGrad(shared_sampler_linear_repeat(), uv, dx, dy);
    if (samplerMode == SPRITE2D_SAMPLER_NEAREST_CLAMP)
        return tex.SampleGrad(shared_sampler_nearest_clamp(), uv, dx, dy);
    if (samplerMode == SPRITE2D_SAMPLER_NEAREST_REPEAT)
        return tex.SampleGrad(shared_sampler_nearest_repeat(), uv, dx, dy);
    return tex.SampleGrad(shared_sampler_linear_clamp(), uv, dx, dy);
}

public float4 sprite2DShade(Sprite2DVsOut input)
{
    float4 texel = sprite2DSampleTexture(input.TextureIndex, input.Flags, input.Uv);
    // Compute derivatives before branching on shading kind so quad helper lanes stay valid.
    float sdfWidth = max(0.5 * length(float2(ddx(texel.r), ddy(texel.r))), 1.0 / 255.0);
    float sdfAlpha = smoothstep(0.5 - sdfWidth, 0.5 + sdfWidth, texel.r);

    uint kind = (input.Flags >> SPRITE2D_KIND_SHIFT) & SPRITE2D_KIND_MASK;
    if (kind == SPRITE2D_KIND_DISTANCE_GLYPH || kind == SPRITE2D_KIND_BITMAP_GLYPH)
    {
        float cov = kind == SPRITE2D_KIND_DISTANCE_GLYPH ? sdfAlpha : texel.r;
        float a = input.Color.a * cov;
        return float4(input.Color.rgb * a, a);
    }

    if (kind == SPRITE2D_KIND_PREMULTIPLIED)
    {
        return float4(texel.rgb * (input.Color.rgb * input.Color.a), texel.a * input.Color.a);
    }

    float4 combined = texel * input.Color;
    return float4(combined.rgb * combined.a, combined.a);
}
