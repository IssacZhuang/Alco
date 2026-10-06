using System.Numerics;
using System.Runtime.CompilerServices;

namespace Alco.Graphics.AlcoGpu;

/// <summary>Describes a color attachment's native format and default clear value.</summary>
internal struct ColorAttachmentInfo
{
    /// <summary>The color attachment format.</summary>
    public PixelFormat Format;
    /// <summary>The default clear color.</summary>
    public Vector4 ClearColor;
}

/// <summary>Describes depth-stencil aspect presence, default clears, and native read-only state.</summary>
internal struct DepthAttachmentInfo
{
    /// <summary>The depth-stencil attachment format.</summary>
    public PixelFormat Format;
    /// <summary>Whether the format contains a depth aspect, independently of read-only state.</summary>
    public bool HasDepth;
    /// <summary>Whether the format contains a stencil aspect, independently of read-only state.</summary>
    public bool HasStencil;
    /// <summary>The default depth clear value.</summary>
    public float ClearDepth;
    /// <summary>Whether depth operations are omitted for a missing or read-only aspect.</summary>
    public bool IsDepthReadOnly;
    /// <summary>The default stencil clear value.</summary>
    public uint ClearStencil;
    /// <summary>Whether stencil operations are omitted for a missing or read-only aspect.</summary>
    public bool IsStencilReadOnly;
}

/// <summary>
/// Pure metadata describing a render pass attachment set: formats, clear values
/// and depth read-only state. No native object is created; the data is consumed
/// when frame buffers bake pass descriptors.
/// </summary>
internal sealed class AlcoGpuAttachmentLayout : GPUAttachmentLayout
{
    #region Properties
    private readonly ColorAttachmentInfo[] _colorInfos;
    private readonly DepthAttachmentInfo? _depthInfo;

    #endregion

    #region Abstract Implementation
    protected override GPUDevice Device { get; }

    protected override void Dispose(bool disposing)
    {
        // Nothing to do because only meta data inside
    }

    #endregion

    #region AlcoGpu Implementation

    internal ReadOnlySpan<ColorAttachmentInfo> ColorInfos
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _colorInfos;
    }

    internal DepthAttachmentInfo? DepthInfo
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _depthInfo;
    }

    internal AlcoGpuAttachmentLayout(AlcoGpuDevice device, in AttachmentLayoutDescriptor descriptor) : base(descriptor)
    {
        Device = device;

        int colorCount = descriptor.Colors.Length;

        _colorInfos = new ColorAttachmentInfo[colorCount];
        for (int i = 0; i < colorCount; i++)
        {
            ColorAttachment color = descriptor.Colors[i];
            _colorInfos[i] = new ColorAttachmentInfo
            {
                Format = color.Format,
                ClearColor = color.ClearColor,
            };
        }

        if (descriptor.Depth.HasValue)
        {
            DepthAttachment depth = descriptor.Depth.Value;
            bool hasDepth = PixelFormatUtility.IsDepthFormat(depth.Format);
            bool hasStencil = PixelFormatUtility.HasStencil(depth.Format) || depth.Format == PixelFormat.Stencil8;
            _depthInfo = new DepthAttachmentInfo
            {
                Format = depth.Format,
                HasDepth = hasDepth,
                HasStencil = hasStencil,
                ClearDepth = depth.ClearDepth,
                IsDepthReadOnly = depth.ReadOnly || !hasDepth,
                ClearStencil = depth.ClearStencil,
                IsStencilReadOnly = depth.ReadOnly || !hasStencil,
            };
        }
    }

    #endregion
}
