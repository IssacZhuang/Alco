using System.Numerics;
using System.Runtime.InteropServices;
using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>Sampler selection encoded into <see cref="Sprite2DQuad.Flags"/>.</summary>
public enum Sprite2DSamplerMode : uint
{
    /// <summary>Bilinear filtering with edge clamping.</summary>
    LinearClamp = 0,

    /// <summary>Bilinear filtering with repeating wrap.</summary>
    LinearRepeat = 1,

    /// <summary>Point filtering with edge clamping.</summary>
    NearestClamp = 2,

    /// <summary>Point filtering with repeating wrap.</summary>
    NearestRepeat = 3,
}

/// <summary>Pixel shading mode encoded into <see cref="Sprite2DQuad.Flags"/>.</summary>
public enum Sprite2DShadingKind : uint
{
    /// <summary>Straight-alpha RGBA texture tinted by vertex color and premultiplied on output.</summary>
    Straight = 0,

    /// <summary>Premultiplied-alpha texture tinted by vertex color.</summary>
    Premultiplied = 1,

    /// <summary>Single-channel signed-distance-field font glyph rendered as premultiplied tint.</summary>
    DistanceGlyph = 2,

    /// <summary>Single-channel coverage bitmap glyph rendered as premultiplied tint.</summary>
    BitmapGlyph = 3,
}

/// <summary>Helpers for packing <see cref="Sprite2DQuad.Flags"/> and resolving sampler modes.</summary>
public static class Sprite2DFlagsUtility
{
    /// <summary>Packs sampler and shading modes into the quad flags word.</summary>
    /// <param name="samplerMode">The sampler mode.</param>
    /// <param name="shadingKind">The shading mode.</param>
    /// <returns>The packed 32-bit flags word.</returns>
    public static uint PackFlags(Sprite2DSamplerMode samplerMode, Sprite2DShadingKind shadingKind)
    {
        return ((uint)samplerMode & 3u) | (((uint)shadingKind & 7u) << 2);
    }

    /// <summary>Maps engine filter and address settings to a <see cref="Sprite2DSamplerMode"/>.</summary>
    /// <param name="filter">The texture filter.</param>
    /// <param name="address">The texture address mode.</param>
    /// <returns>The matching sampler mode.</returns>
    public static Sprite2DSamplerMode ResolveSamplerMode(Filter filter, AddressMode address = AddressMode.ClampToEdge)
    {
        bool repeat = address == AddressMode.Repeat;
        if (filter == Filter.Nearest)
        {
            return repeat ? Sprite2DSamplerMode.NearestRepeat : Sprite2DSamplerMode.NearestClamp;
        }

        return repeat ? Sprite2DSamplerMode.LinearRepeat : Sprite2DSamplerMode.LinearClamp;
    }
}

/// <summary>Per-quad GPU record read by <c>Sprite2DDefault</c> and <c>Sprite2DPass</c> shaders (80 bytes).</summary>
[StructLayout(LayoutKind.Sequential)]
public struct Sprite2DQuad
{
    /// <summary>Top-left quad corner in world space.</summary>
    public Vector2 Origin;

    /// <summary>Vector from top-left to top-right corner.</summary>
    public Vector2 Right;

    /// <summary>Vector from top-left to bottom-left corner.</summary>
    public Vector2 Down;

    /// <summary>World-space depth coordinate.</summary>
    public float Depth;

    /// <summary>Bindless sampled texture descriptor index.</summary>
    public uint TextureIndex;

    /// <summary>Normalized UV rectangle (x, y, width, height).</summary>
    public Vector4 UvRect;

    /// <summary>Linear RGBA tint color.</summary>
    public Vector4 Color;

    /// <summary>Packed sampler and shading flags built by <see cref="Sprite2DFlagsUtility.PackFlags"/>.</summary>
    public uint Flags;

    /// <summary>Padding to maintain 16-byte alignment.</summary>
    public uint Pad0;

    /// <summary>Padding to maintain 16-byte alignment.</summary>
    public uint Pad1;

    /// <summary>Padding to maintain 16-byte alignment.</summary>
    public uint Pad2;
}

/// <summary>Root header matching <c>Sprite2DPassHeader</c> in Slang (80 bytes).</summary>
[StructLayout(LayoutKind.Sequential)]
public struct Sprite2DPassHeader
{
    /// <summary>Row-major camera view-projection matrix.</summary>
    public Matrix4x4 ViewProjection;

    /// <summary>View into the current run's slice of <see cref="Sprite2DQuad"/> records.</summary>
    public GPUBufferView Quads;

    /// <summary>Elapsed pass time in seconds.</summary>
    public float Time;

    /// <summary>Normalized UV span corresponding to the full SDF spread range on an atlas page.</summary>
    public float SdfUnitRange;
}

/// <summary>Per-frame submission telemetry produced by <see cref="SpriteBatch2D.Record"/>.</summary>
/// <param name="QuadCount">Total quads emitted during the batch.</param>
/// <param name="DrawCalls">Number of instanced draw calls recorded.</param>
/// <param name="CustomMaterialDraws">Number of draws that used a custom <c>ISprite2DSurface</c> material.</param>
/// <param name="TextGlyphCount">Number of text glyph quads emitted.</param>
public readonly record struct SpriteBatch2DStats(
    int QuadCount,
    int DrawCalls,
    int CustomMaterialDraws,
    int TextGlyphCount);
