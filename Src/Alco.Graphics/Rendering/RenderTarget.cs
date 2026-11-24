using Alco.Graphics.LowLevel;

namespace Alco.Graphics;

/// <summary>An attachment target view wrapping a mip/slice of a <see cref="Graphics.Texture"/> or a swapchain image.</summary>
public readonly struct RenderTarget : IEquatable<RenderTarget>
{
    internal readonly GPURenderView? View;
    private readonly Texture? _texture;
    private readonly Extent2D _extent;

    internal RenderTarget(GPURenderView? view, Texture? texture, Extent2D extent)
    {
        View = view;
        _texture = texture;
        _extent = extent;
    }

    /// <summary>The backing <see cref="Graphics.Texture"/>, or <see langword="null"/> for swapchain images.</summary>
    public Texture? Texture => _texture;

    /// <summary>Whether this target references a valid attachment view.</summary>
    public bool IsValid => View != null;

    /// <summary>The attachment extent in pixels, or zero when unavailable.</summary>
    public Extent2D Extent => _extent;

    /// <summary>The attachment pixel format, or <see cref="Format.Undefined"/> when unavailable.</summary>
    public Format Format => View != null ? View.Format : Format.Undefined;

    /// <summary>The attachment raster sample count, or zero when unavailable.</summary>
    public uint SampleCount => View != null ? View.SampleCount : 0;

    /// <summary>Creates a render target referencing mip 0, slice 0 of <paramref name="texture"/>.</summary>
    /// <param name="texture">The attachment texture.</param>
    /// <returns>The mip-zero, slice-zero render target.</returns>
    public static RenderTarget FromTexture(Texture texture)
    {
        ArgumentNullException.ThrowIfNull(texture);
        return texture.GetRenderTarget();
    }

    /// <summary>Creates a render target referencing a specific mip level and array slice of <paramref name="texture"/>.</summary>
    /// <param name="texture">The attachment texture.</param>
    /// <param name="mipLevel">The zero-based mip level.</param>
    /// <param name="arraySlice">The zero-based physical array slice or cubemap face.</param>
    /// <returns>The render target view.</returns>
    public static RenderTarget FromTexture(Texture texture, uint mipLevel, uint arraySlice = 0)
    {
        ArgumentNullException.ThrowIfNull(texture);
        return texture.GetRenderTarget(mipLevel, arraySlice);
    }

    /// <summary>Implicitly converts a texture to its mip-zero, slice-zero render target.</summary>
    public static implicit operator RenderTarget(Texture? texture) => texture != null ? FromTexture(texture) : default;

    /// <inheritdoc />
    public bool Equals(RenderTarget other) => ReferenceEquals(View, other.View);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is RenderTarget other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => View?.GetHashCode() ?? 0;

    /// <summary>Tests whether two render targets wrap the same attachment view.</summary>
    public static bool operator ==(RenderTarget left, RenderTarget right) => left.Equals(right);

    /// <summary>Tests whether two render targets wrap different attachment views.</summary>
    public static bool operator !=(RenderTarget left, RenderTarget right) => !left.Equals(right);
}
