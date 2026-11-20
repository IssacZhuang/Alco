// Backend-independent helpers for command recording, alignment, and diagnostics.

using System;
using System.Diagnostics;

using Alco.Graphics;

namespace Alco.Graphics.LowLevel;

internal static unsafe partial class Backend
{
    public const int QueueTypeCount = 3; // General, compute-only, copy-only.
    public const uint MaxColorAttachments = 8;
    public const ulong GpuAllocationAlignment = 16;
    public const int FormatCount = (int)Format.Undefined;
    // Standard bindless heap capacities, uniform across backends and clamped by each backend's device limits.
    public const uint BindlessTextureDescriptorCapacity = 1_000_000;
    public const uint BindlessSamplerDescriptorCapacity = 1024;

    public static void RequireError(Error error, string operation)
    {
        if (error != Error.None)
        {
            Gpu.ReportError($"{operation} failed (graphics error {(uint)error}).", true);
            Debug.Assert(false, "unexpected graphics API failure");
            Environment.FailFast($"{operation} failed (graphics error {(uint)error}).");
        }
    }

    public static ulong AlignUp(ulong value, ulong Alignment)
    {
        Debug.Assert(Alignment != 0);
        return ((value + Alignment - 1) / Alignment) * Alignment;
    }

    public static ulong DivideUp(ulong value, ulong divisor)
    {
        Debug.Assert(divisor != 0);
        return value / divisor + (value % divisor != 0 ? 1u : 0u);
    }

    [Conditional("DEBUG")]
    internal static void AssertTextureCopyRegion(Format format, Extent3D mipExtent, Extent3D origin, Extent3D extent)
    {
        TextureFormatInfo info = Gpu.GetTextureFormatInfo(format);
        Debug.Assert(!(info.Depth && info.Stencil), "combined depth/stencil transfers are unsupported");
        if (!info.Depth && !info.Stencil) return;
        Debug.Assert(origin.X == 0 && origin.Y == 0 && origin.Z == 0, "depth/stencil transfers require zero texture origins");
        Debug.Assert((extent.X == 0 || extent.X == mipExtent.X) && (extent.Y == 0 || extent.Y == mipExtent.Y) &&
            (extent.Z == 0 || extent.Z == mipExtent.Z), "depth/stencil transfers cover complete mip subresources");
    }

    public static bool HasFlag(TextureUsage value, TextureUsage flag) => (value & flag) != 0;
    public static bool HasFlag(Stage value, Stage flag) => (value & flag) != 0;
    public static bool HasFlag(Access value, Access flag) => (value & flag) != 0;

    internal static bool SupportsTextureSamples(in TextureDesc desc)
    {
        if (desc.SampleCount == 1) return true;
        if (desc.SampleCount != 4 || desc.MipLevels != 1 || desc.MutableFormat || desc.Extent.Z != 1 ||
            desc.Type is not (TextureType.TwoD or TextureType.TwoDArray) || (desc.Type == TextureType.TwoD && desc.LayerCount != 1)) return false;
        return (desc.Usage == TextureUsage.ColorAttachment && SupportsColorResolve(desc.Format)) ||
               (desc.Usage == TextureUsage.DepthStencilAttachment && SupportsMultisampleDepthStencil(desc.Format));
    }

    internal static void RequireTextureSamples(in TextureDesc desc)
    {
        if (!SupportsTextureSamples(in desc))
            throw new GPUException(Error.Unsupported, "The texture description does not support the requested sample count.");
    }

    internal static bool SupportsColorResolve(Format format) => format is Format.Rgba8Unorm or Format.Bgra8Unorm or Format.Rgba16Float;

    internal static bool IsColorResolveTarget(TextureType type, TextureUsage usage) => type is TextureType.TwoD or TextureType.TwoDArray &&
        (usage & (TextureUsage.ColorAttachment | TextureUsage.Storage)) == TextureUsage.ColorAttachment;

    private static bool SupportsMultisampleDepthStencil(Format format) => format is Format.D16Unorm or Format.D32Float or Format.D32FloatS8Uint;

    internal static void RequireRasterSamples(uint sampleCount, ReadOnlySpan<ColorTargetDesc> colorTargets, Format depth, Format stencil)
    {
        if (sampleCount == 1) return;
        if (sampleCount != 4)
            throw new GPUException(Error.Unsupported, "Raster pipelines require a sample count of 1 or 4.");
        foreach (ref readonly ColorTargetDesc color in colorTargets)
            if (!SupportsColorResolve(color.Format))
                throw new GPUException(Error.Unsupported, $"Color format {color.Format} does not support 4脳 rasterization.");
        if ((depth != Format.Undefined && !SupportsMultisampleDepthStencil(depth)) ||
            (stencil != Format.Undefined && !SupportsMultisampleDepthStencil(stencil)))
            throw new GPUException(Error.Unsupported, "The depth or stencil format does not support 4脳 rasterization.");
    }

    // memmove semantics for native staging buffers; spans handle overlap correctly.
    public static void NativeCopy(void* destination, void* source, nuint byteCount)
    {
        if (byteCount == 0) return;
        new Span<byte>(source, (int)byteCount).CopyTo(new Span<byte>(destination, (int)byteCount));
    }
}
