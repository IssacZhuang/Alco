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
    public float SdfUnitRange;
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

float4 sprite2DSampleTextureGrad(uint textureIndex, uint flags, float2 uv, float2 dx, float2 dy)
{
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

public float4 sprite2DSampleTexture(uint textureIndex, uint flags, float2 uv)
{
    // Compute derivatives outside divergent sampler branches so VulkanCompact stays compliant
    // without shaderSamplerArrayNonUniformIndexing and preserves quad helper lane derivatives.
    float2 dx = ddx(uv);
    float2 dy = ddy(uv);
    return sprite2DSampleTextureGrad(textureIndex, flags, uv, dx, dy);
}

float sprite2DEvaluateSdfCoverage(
    Texture2D<float4> tex,
    float2 uv,
    float2 duvDx,
    float2 duvDy,
    float sdfUnitRange)
{
    float uvPerPx = sqrt(0.5 * (dot(duvDx, duvDx) + dot(duvDy, duvDy)));
    float unitRange = sdfUnitRange > 0.0 ? sdfUnitRange : (12.0 / 1024.0);
    float screenPxRange = max(unitRange / max(uvPerPx, 1e-6), 1.0);
    float tapScale = screenPxRange * 2.0;

    float2 d1 = 0.125 * duvDx + 0.375 * duvDy;
    float2 d2 = 0.375 * duvDx - 0.125 * duvDy;

    float s0 = tex.SampleLevel(shared_sampler_linear_clamp(), uv - d1, 0.0).r;
    float s1 = tex.SampleLevel(shared_sampler_linear_clamp(), uv + d2, 0.0).r;
    float s2 = tex.SampleLevel(shared_sampler_linear_clamp(), uv + d1, 0.0).r;
    float s3 = tex.SampleLevel(shared_sampler_linear_clamp(), uv - d2, 0.0).r;

    float c0 = saturate((s0 - 0.5) * tapScale + 0.5);
    float c1 = saturate((s1 - 0.5) * tapScale + 0.5);
    float c2 = saturate((s2 - 0.5) * tapScale + 0.5);
    float c3 = saturate((s3 - 0.5) * tapScale + 0.5);
    return 0.25 * (c0 + c1 + c2 + c3);
}

public float4 sprite2DShade(Sprite2DVsOut input, float sdfUnitRange = 12.0 / 1024.0)
{
    // Compute UV derivatives before branching on shading kind so quad helper lanes stay valid.
    float2 duvDx = ddx(input.Uv);
    float2 duvDy = ddy(input.Uv);

    uint kind = (input.Flags >> SPRITE2D_KIND_SHIFT) & SPRITE2D_KIND_MASK;
    if (kind == SPRITE2D_KIND_DISTANCE_GLYPH)
    {
        Texture2D<float4> tex = gpu_texture<Texture2D<float4>>(gpu_nonuniform_index(input.TextureIndex));
        float cov = sprite2DEvaluateSdfCoverage(tex, input.Uv, duvDx, duvDy, sdfUnitRange);
        float a = input.Color.a * cov;
        return float4(input.Color.rgb * a, a);
    }

    float4 texel = sprite2DSampleTextureGrad(input.TextureIndex, input.Flags, input.Uv, duvDx, duvDy);
    if (kind == SPRITE2D_KIND_BITMAP_GLYPH)
    {
        float a = input.Color.a * texel.r;
        return float4(input.Color.rgb * a, a);
    }

    if (kind == SPRITE2D_KIND_PREMULTIPLIED)
    {
        return float4(texel.rgb * (input.Color.rgb * input.Color.a), texel.a * input.Color.a);
    }

    float4 combined = texel * input.Color;
    return float4(combined.rgb * combined.a, combined.a);
}
