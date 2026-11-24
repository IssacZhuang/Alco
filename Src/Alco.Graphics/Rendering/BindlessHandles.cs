using System;
using System.Runtime.InteropServices;

namespace Alco.Graphics;

/// <summary>Eight-byte shader-visible buffer view identifying a backing buffer and byte offset.</summary>
[StructLayout(LayoutKind.Sequential, Size = 8)]
public record struct GPUBufferView
{
    /// <summary>The device-local backing-buffer index; zero denotes an empty view.</summary>
    public uint Index;

    /// <summary>The byte offset within the backing buffer.</summary>
    public uint Offset;

    /// <summary>Gets whether this view references a valid backing buffer.</summary>
    public readonly bool IsValid => Index != 0;

    /// <summary>Advances the view within its backing buffer.</summary>
    /// <param name="byteOffset">The additional offset in bytes.</param>
    /// <returns>A view of the same buffer at the advanced offset.</returns>
    public readonly GPUBufferView OffsetBy(uint byteOffset)
    {
        return new GPUBufferView { Index = Index, Offset = Offset + byteOffset };
    }
}

/// <summary>Four-byte bindless texture descriptor handle matching Slang <c>Core.GPUTextureId</c>.</summary>
[StructLayout(LayoutKind.Sequential, Size = sizeof(uint))]
public readonly struct GPUTextureId : IEquatable<GPUTextureId>
{
    /// <summary>The bindless descriptor index (<see cref="Texture.NoIndex"/> when invalid).</summary>
    public readonly uint Index;

    /// <summary>Initializes a handle from a raw bindless descriptor index.</summary>
    /// <param name="index">The descriptor index.</param>
    public GPUTextureId(uint index)
    {
        Index = index;
    }

    /// <summary>Gets the sentinel invalid texture handle.</summary>
    public static GPUTextureId Invalid => new(Texture.NoIndex);

    /// <summary>Gets whether <see cref="Index"/> does not equal <see cref="Texture.NoIndex"/>.</summary>
    public bool IsValid => Index != Texture.NoIndex;

    /// <summary>Creates a sampled texture handle, or <see cref="Invalid"/> when unavailable.</summary>
    /// <param name="texture">The source texture, or null.</param>
    /// <returns>The sampled descriptor handle, or <see cref="Invalid"/>.</returns>
    public static GPUTextureId FromSampled(Texture? texture)
    {
        if (texture == null || texture.IsDisposed)
            return Invalid;

        return new GPUTextureId(texture.SampledIndex);
    }

    /// <summary>Creates a sampled texture handle interpreted with <paramref name="viewFormat"/>, or <see cref="Invalid"/> when unavailable.</summary>
    /// <param name="texture">The source texture, or null.</param>
    /// <param name="viewFormat">The view format, or <see cref="Format.Undefined"/> for the texture's native format.</param>
    /// <returns>The sampled descriptor handle, or <see cref="Invalid"/>.</returns>
    public static GPUTextureId FromSampled(Texture? texture, Format viewFormat)
    {
        if (texture == null || texture.IsDisposed || (texture.Desc.Usage & TextureUsage.Sampled) == 0)
            return Invalid;

        return new GPUTextureId(texture.GetSampledIndex(viewFormat));
    }

    /// <summary>Creates a storage texture handle for <paramref name="mipLevel"/>, or <see cref="Invalid"/> when unavailable.</summary>
    /// <param name="texture">The source texture, or null.</param>
    /// <param name="mipLevel">The zero-based mip level.</param>
    /// <returns>The storage descriptor handle, or <see cref="Invalid"/>.</returns>
    public static GPUTextureId FromStorage(Texture? texture, uint mipLevel = 0)
    {
        if (texture == null || texture.IsDisposed || mipLevel >= texture.MipLevels)
            return Invalid;

        return new GPUTextureId(texture.GetStorageIndex(mipLevel));
    }

    /// <inheritdoc />
    public bool Equals(GPUTextureId other) => Index == other.Index;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is GPUTextureId other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => (int)Index;

    /// <inheritdoc />
    public override string ToString() => IsValid ? $"GPUTextureId({Index})" : "GPUTextureId(Invalid)";

    /// <summary>Tests whether two texture handles refer to the same descriptor slot.</summary>
    public static bool operator ==(GPUTextureId left, GPUTextureId right) => left.Equals(right);

    /// <summary>Tests whether two texture handles refer to different descriptor slots.</summary>
    public static bool operator !=(GPUTextureId left, GPUTextureId right) => !left.Equals(right);
}

/// <summary>Four-byte bindless sampler descriptor handle matching Slang <c>Core.GPUSamplerId</c>.</summary>
[StructLayout(LayoutKind.Sequential, Size = sizeof(uint))]
public readonly struct GPUSamplerId : IEquatable<GPUSamplerId>
{
    /// <summary>The bindless descriptor index (<see cref="Texture.NoIndex"/> when invalid).</summary>
    public readonly uint Index;

    /// <summary>Initializes a handle from a raw bindless descriptor index.</summary>
    /// <param name="index">The descriptor index.</param>
    public GPUSamplerId(uint index)
    {
        Index = index;
    }

    /// <summary>Gets the sentinel invalid sampler handle.</summary>
    public static GPUSamplerId Invalid => new(Texture.NoIndex);

    /// <summary>Gets whether <see cref="Index"/> does not equal <see cref="Texture.NoIndex"/>.</summary>
    public bool IsValid => Index != Texture.NoIndex;

    /// <summary>Creates a sampler handle, or <see cref="Invalid"/> when unavailable.</summary>
    /// <param name="sampler">The source sampler, or null.</param>
    /// <returns>The sampler descriptor handle, or <see cref="Invalid"/>.</returns>
    public static GPUSamplerId From(Sampler? sampler)
    {
        if (sampler == null || sampler.IsDisposed)
            return Invalid;

        return new GPUSamplerId(sampler.Index);
    }

    /// <inheritdoc />
    public bool Equals(GPUSamplerId other) => Index == other.Index;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is GPUSamplerId other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => (int)Index;

    /// <inheritdoc />
    public override string ToString() => IsValid ? $"GPUSamplerId({Index})" : "GPUSamplerId(Invalid)";

    /// <summary>Tests whether two sampler handles refer to the same descriptor slot.</summary>
    public static bool operator ==(GPUSamplerId left, GPUSamplerId right) => left.Equals(right);

    /// <summary>Tests whether two sampler handles refer to different descriptor slots.</summary>
    public static bool operator !=(GPUSamplerId left, GPUSamplerId right) => !left.Equals(right);
}
