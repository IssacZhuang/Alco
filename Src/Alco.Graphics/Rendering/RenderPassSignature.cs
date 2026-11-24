using System.Diagnostics;
using System.Runtime.CompilerServices;
using Alco.Graphics.LowLevel;

namespace Alco.Graphics;

/// <summary>Compact value-type description of a render pass's attachment formats and sample count.</summary>
public readonly struct RenderPassSignature : IEquatable<RenderPassSignature>
{
    private readonly ulong _colorFormats;
    private readonly uint _meta;

    /// <summary>Initializes a render-pass signature from color, depth, stencil formats and sample count.</summary>
    /// <param name="colorFormats">The render pass color target formats (at most eight).</param>
    /// <param name="depthFormat">The render pass depth attachment format.</param>
    /// <param name="stencilFormat">The render pass stencil attachment format.</param>
    /// <param name="sampleCount">The raster sample count; zero defaults to one.</param>
    public RenderPassSignature(ReadOnlySpan<Format> colorFormats, Format depthFormat = Format.Undefined,
        Format stencilFormat = Format.Undefined, uint sampleCount = 1)
    {
        if (colorFormats.Length > Backend.MaxColorAttachments)
            throw new ArgumentOutOfRangeException(nameof(colorFormats), $"Render passes support at most {Backend.MaxColorAttachments} color attachments.");
        uint normalizedSamples = NormalizeSampleCount(sampleCount);

        ulong packedColors = 0;
        for (int index = 0; index < colorFormats.Length; ++index)
            packedColors |= (ulong)(byte)colorFormats[index] << (index * 8);

        _colorFormats = packedColors;
        _meta = PackMeta(colorFormats.Length, depthFormat, stencilFormat, normalizedSamples);
    }

    /// <summary>Initializes a single-color-target render-pass signature.</summary>
    /// <param name="colorFormat">The single color target format.</param>
    /// <param name="depthFormat">The render pass depth attachment format.</param>
    /// <param name="stencilFormat">The render pass stencil attachment format.</param>
    /// <param name="sampleCount">The raster sample count; zero defaults to one.</param>
    public RenderPassSignature(Format colorFormat, Format depthFormat = Format.Undefined,
        Format stencilFormat = Format.Undefined, uint sampleCount = 1)
    {
        uint normalizedSamples = NormalizeSampleCount(sampleCount);
        _colorFormats = (byte)colorFormat;
        _meta = PackMeta(1, depthFormat, stencilFormat, normalizedSamples);
    }

    private RenderPassSignature(ulong colorFormats, uint meta)
    {
        _colorFormats = colorFormats;
        _meta = meta;
    }

    /// <summary>Derives a render-pass signature from the attachment targets in <paramref name="desc"/>.</summary>
    /// <param name="desc">The high-level render pass attachment description.</param>
    /// <returns>The derived render-pass signature.</returns>
    public static RenderPassSignature From(scoped in RenderingDesc desc)
    {
        int colorCount = desc.Colors.Length;
        if (colorCount > Backend.MaxColorAttachments)
            throw new ArgumentOutOfRangeException(nameof(desc), $"Render passes support at most {Backend.MaxColorAttachments} color attachments.");

        ulong packedColors = 0;
        uint sampleCount = 0;
        for (int index = 0; index < colorCount; ++index)
        {
            RenderTarget target = desc.Colors[index].Target;
            Format format = target.Format;
            packedColors |= (ulong)(byte)format << (index * 8);
            if (!target.IsValid)
                continue;
            uint viewSamples = target.SampleCount;
            if (sampleCount == 0)
                sampleCount = viewSamples;
            else
                Debug.Assert(viewSamples == sampleCount, "All attachments in a render pass must share the same sample count.");
        }

        RenderTarget depthTarget = desc.Depth.Target;
        Format depthFormat = depthTarget.Format;
        if (depthTarget.IsValid)
        {
            uint depthSamples = depthTarget.SampleCount;
            if (sampleCount == 0)
                sampleCount = depthSamples;
            else
                Debug.Assert(depthSamples == sampleCount, "All attachments in a render pass must share the same sample count.");
        }

        RenderTarget stencilTarget = desc.Stencil.Target;
        Format stencilFormat = stencilTarget.Format;
        if (stencilTarget.IsValid)
        {
            uint stencilSamples = stencilTarget.SampleCount;
            if (sampleCount == 0)
                sampleCount = stencilSamples;
            else
                Debug.Assert(stencilSamples == sampleCount, "All attachments in a render pass must share the same sample count.");
        }

        uint normalizedSamples = sampleCount == 0 ? 1u : NormalizeSampleCount(sampleCount);
        return new RenderPassSignature(packedColors, PackMeta(colorCount, depthFormat, stencilFormat, normalizedSamples));
    }

    internal static RenderPassSignature From(scoped in GPURenderingDesc desc)
    {
        int colorCount = desc.Colors.Length;
        if (colorCount > Backend.MaxColorAttachments)
            throw new ArgumentOutOfRangeException(nameof(desc), $"Render passes support at most {Backend.MaxColorAttachments} color attachments.");

        ulong packedColors = 0;
        uint sampleCount = 0;
        for (int index = 0; index < colorCount; ++index)
        {
            GPURenderView? view = desc.Colors[index].RenderView;
            Format format = view != null ? view.Format : Format.Undefined;
            packedColors |= (ulong)(byte)format << (index * 8);
            if (view == null)
                continue;
            uint viewSamples = view.SampleCount;
            if (sampleCount == 0)
                sampleCount = viewSamples;
            else
                Debug.Assert(viewSamples == sampleCount, "All attachments in a render pass must share the same sample count.");
        }

        GPURenderView? depthView = desc.Depth.RenderView;
        Format depthFormat = depthView != null ? depthView.Format : Format.Undefined;
        if (depthView != null)
        {
            uint depthSamples = depthView.SampleCount;
            if (sampleCount == 0)
                sampleCount = depthSamples;
            else
                Debug.Assert(depthSamples == sampleCount, "All attachments in a render pass must share the same sample count.");
        }

        GPURenderView? stencilView = desc.Stencil.RenderView;
        Format stencilFormat = stencilView != null ? stencilView.Format : Format.Undefined;
        if (stencilView != null)
        {
            uint stencilSamples = stencilView.SampleCount;
            if (sampleCount == 0)
                sampleCount = stencilSamples;
            else
                Debug.Assert(stencilSamples == sampleCount, "All attachments in a render pass must share the same sample count.");
        }

        uint normalizedSamples = sampleCount == 0 ? 1u : NormalizeSampleCount(sampleCount);
        return new RenderPassSignature(packedColors, PackMeta(colorCount, depthFormat, stencilFormat, normalizedSamples));
    }

    /// <summary>The number of color targets (0..8).</summary>
    public int ColorCount => (int)(_meta & 0xffu);

    /// <summary>The depth attachment format, or <see cref="Format.Undefined"/> when absent.</summary>
    public Format DepthFormat => (Format)(byte)(((_meta >> 8) & 0xffu) ^ (uint)Format.Undefined);

    /// <summary>The stencil attachment format, or <see cref="Format.Undefined"/> when absent.</summary>
    public Format StencilFormat => (Format)(byte)(((_meta >> 16) & 0xffu) ^ (uint)Format.Undefined);

    /// <summary>The raster sample count (at least 1).</summary>
    public uint SampleCount => ((_meta >> 24) & 0xffu) ^ 1u;

    internal ulong ColorFormatsPacked => _colorFormats;

    internal uint MetaPacked => _meta;

    /// <summary>Gets the color format at <paramref name="index"/>.</summary>
    /// <param name="index">The zero-based color attachment index.</param>
    /// <returns>The color target format.</returns>
    public Format GetColorFormat(int index)
    {
        if ((uint)index >= (uint)ColorCount)
            throw new ArgumentOutOfRangeException(nameof(index));
        return (Format)(byte)(_colorFormats >> (index * 8));
    }

    /// <summary>Copies all color target formats into <paramref name="destination"/>.</summary>
    /// <param name="destination">A span whose length is at least <see cref="ColorCount"/>.</param>
    public void CopyColorFormats(Span<Format> destination)
    {
        int count = ColorCount;
        if (destination.Length < count)
            throw new ArgumentException($"Destination span must hold at least {count} formats.", nameof(destination));
        for (int index = 0; index < count; ++index)
            destination[index] = (Format)(byte)(_colorFormats >> (index * 8));
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(RenderPassSignature other) => _colorFormats == other._colorFormats && _meta == other._meta;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is RenderPassSignature other && Equals(other);

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override int GetHashCode()
    {
        ulong hash = _colorFormats ^ ((ulong)_meta * 0x9E3779B97F4A7C15ul);
        hash ^= hash >> 30;
        hash *= 0xBF58476D1CE4E5B9ul;
        hash ^= hash >> 27;
        return unchecked((int)(hash ^ (hash >> 31)));
    }

    /// <summary>Tests whether two signatures are equal.</summary>
    public static bool operator ==(RenderPassSignature left, RenderPassSignature right) => left.Equals(right);

    /// <summary>Tests whether two signatures differ.</summary>
    public static bool operator !=(RenderPassSignature left, RenderPassSignature right) => !left.Equals(right);

    private static uint NormalizeSampleCount(uint sampleCount)
    {
        uint normalized = sampleCount == 0 ? 1u : sampleCount;
        if (normalized > 255u)
            throw new ArgumentOutOfRangeException(nameof(sampleCount), "Sample count must not exceed 255.");
        return normalized;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint PackMeta(int colorCount, Format depthFormat, Format stencilFormat, uint normalizedSamples)
    {
        uint depthXor = (uint)(byte)depthFormat ^ (uint)Format.Undefined;
        uint stencilXor = (uint)(byte)stencilFormat ^ (uint)Format.Undefined;
        uint sampleXor = normalizedSamples ^ 1u;
        return (uint)colorCount | (depthXor << 8) | (stencilXor << 16) | (sampleXor << 24);
    }
}
