using System.Numerics;
using System.Runtime.CompilerServices;

namespace Alco.Graphics.AlcoGpu;

internal struct AlcoColorAttachmentInfo
{
    public PixelFormat Format;
    public Vector4 ClearColor;
}

internal struct AlcoDepthAttachmentInfo
{
    public PixelFormat Format;
    public float ClearDepth;
    public bool IsDepthReadOnly;
    public uint ClearStencil;
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
    private readonly AlcoColorAttachmentInfo[] _colorInfos;
    private readonly AlcoDepthAttachmentInfo? _depthInfo;

    #endregion

    #region Abstract Implementation
    protected override GPUDevice Device { get; }

    protected override void Dispose(bool disposing)
    {
        // Nothing to do because only meta data inside
    }

    #endregion

    #region AlcoGpu Implementation

    internal ReadOnlySpan<AlcoColorAttachmentInfo> ColorInfos
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _colorInfos;
    }

    internal AlcoDepthAttachmentInfo? DepthInfo
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _depthInfo;
    }

    internal AlcoGpuAttachmentLayout(AlcoGpuDevice device, in AttachmentLayoutDescriptor descriptor) : base(descriptor)
    {
        Device = device;

        int colorCount = descriptor.Colors.Length;

        _colorInfos = new AlcoColorAttachmentInfo[colorCount];
        for (int i = 0; i < colorCount; i++)
        {
            ColorAttachment color = descriptor.Colors[i];
            _colorInfos[i] = new AlcoColorAttachmentInfo
            {
                Format = color.Format,
                ClearColor = color.ClearColor,
            };
        }

        if (descriptor.Depth.HasValue)
        {
            DepthAttachment depth = descriptor.Depth.Value;
            _depthInfo = new AlcoDepthAttachmentInfo
            {
                Format = depth.Format,
                ClearDepth = depth.ClearDepth,
                IsDepthReadOnly = depth.ReadOnly,
                ClearStencil = depth.ClearStencil,
                IsStencilReadOnly = depth.ReadOnly,
            };
        }
    }

    #endregion
}
