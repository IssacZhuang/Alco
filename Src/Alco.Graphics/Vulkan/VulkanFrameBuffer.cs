using System.Runtime.CompilerServices;

namespace Alco.Graphics.Vulkan;

/// <summary>
/// Shared implementation for frame buffers: with dynamic rendering a frame buffer is
/// only a set of attachment textures/views; the render pass is assembled per
/// BeginRender call. This base class owns the attachment descriptor helpers.
/// </summary>
internal abstract class VulkanFrameBufferBase : GPUFrameBuffer
{
    protected VulkanFrameBufferBase(in FrameBufferDescriptor descriptor) : base(descriptor)
    {
    }

    protected VulkanFrameBufferBase(string name) : base(name)
    {
    }

    protected static TextureDescriptor BuildColorTextureDescriptor(in ColorAttachment attachment, uint width, uint height, string name)
    {
        return new TextureDescriptor(
            TextureDimension.Texture2D,
            attachment.Format,
            width,
            height,
            1,
            1,
            GPUFrameBuffer.ColorAttachmentUsage,
            1,
            name);
    }

    protected static TextureDescriptor BuildDepthTextureDescriptor(PixelFormat format, uint width, uint height, string name)
    {
        return new TextureDescriptor(
            TextureDimension.Texture2D,
            format,
            width,
            height,
            1,
            1,
            GPUFrameBuffer.DepthAttachmentUsage,
            1,
            name);
    }

    protected static GPUTextureView CreateDepthStencilView(VulkanDevice device, VulkanTexture texture)
    {
        return device.CreateTextureView(new TextureViewDescriptor(texture, aspect: TextureAspect.None));
    }

    protected static GPUTextureView CreateDepthView(VulkanDevice device, VulkanTexture texture)
    {
        return device.CreateTextureView(new TextureViewDescriptor(texture, aspect: TextureAspect.DepthOnly));
    }

    protected static GPUTextureView? CreateStencilView(VulkanDevice device, VulkanTexture texture)
    {
        if (!PixelFormatUtility.HasStencil(texture.PixelFormat))
        {
            return null;
        }
        return device.CreateTextureView(new TextureViewDescriptor(texture, aspect: TextureAspect.StencilOnly));
    }
}

/// <summary>Frame buffer that owns its attachment textures and views.</summary>
internal sealed class VulkanFrameBuffer : VulkanFrameBufferBase
{
    private readonly uint _width;
    private readonly uint _height;
    private readonly VulkanAttachmentLayout _attachmentLayout;

    private readonly VulkanTexture[] _colorTextures;
    private readonly VulkanTextureView[] _colorViews;
    private readonly VulkanTexture? _depthStencilTexture;
    private readonly VulkanTextureView? _depthStencilView;
    private readonly VulkanTextureView? _depthView;
    private readonly VulkanTextureView? _stencilView;

    protected override VulkanDevice Device { get; }

    public override GPUAttachmentLayout AttachmentLayout
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _attachmentLayout;
    }

    public override ReadOnlySpan<GPUTexture> Colors
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _colorTextures;
    }

    public override ReadOnlySpan<GPUTextureView> ColorViews
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _colorViews;
    }

    public override GPUTexture? DepthStencil
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _depthStencilTexture;
    }

    public override GPUTextureView? DepthStencilView
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _depthStencilView;
    }

    public override GPUTextureView? DepthView
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _depthView;
    }

    public override GPUTextureView? StencilView
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _stencilView;
    }

    public override uint Width
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _width;
    }

    public override uint Height
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _height;
    }

    public VulkanFrameBuffer(VulkanDevice device, in FrameBufferDescriptor descriptor) : base(descriptor)
    {
        Device = device;
        _attachmentLayout = (VulkanAttachmentLayout)descriptor.AttachmentLayout;
        _width = descriptor.Width;
        _height = descriptor.Height;

        ReadOnlySpan<ColorAttachment> colors = _attachmentLayout.ColorAttachments;
        _colorTextures = new VulkanTexture[colors.Length];
        _colorViews = new VulkanTextureView[colors.Length];
        for (int i = 0; i < colors.Length; i++)
        {
            _colorTextures[i] = (VulkanTexture)device.CreateTexture(
                BuildColorTextureDescriptor(colors[i], _width, _height, $"{Name}_color_{i}"));
            _colorViews[i] = (VulkanTextureView)device.CreateTextureView(new TextureViewDescriptor(_colorTextures[i]));
        }

        if (_attachmentLayout.DepthInfo.HasValue)
        {
            DepthAttachment depth = _attachmentLayout.DepthInfo.Value;
            _depthStencilTexture = (VulkanTexture)device.CreateTexture(
                BuildDepthTextureDescriptor(depth.Format, _width, _height, $"{Name}_depth"));
            _depthStencilView = (VulkanTextureView)CreateDepthStencilView(device, _depthStencilTexture);
            _depthView = (VulkanTextureView)CreateDepthView(device, _depthStencilTexture);
            _stencilView = (VulkanTextureView?)CreateStencilView(device, _depthStencilTexture);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (!disposing)
        {
            return;
        }

        foreach (GPUTextureView view in _colorViews)
        {
            view.Dispose();
        }
        foreach (GPUTexture texture in _colorTextures)
        {
            texture.Dispose();
        }

        _depthStencilView?.Dispose();
        _depthView?.Dispose();
        _stencilView?.Dispose();
        _depthStencilTexture?.Dispose();
    }
}

/// <summary>
/// Frame buffer composed of externally owned textures and views; nothing is
/// disposed with the frame buffer itself.
/// </summary>
internal sealed class VulkanExternalFrameBuffer : VulkanFrameBufferBase
{
    private readonly uint _width;
    private readonly uint _height;
    private readonly VulkanAttachmentLayout _attachmentLayout;

    private readonly GPUTexture[] _colorTextures;
    private readonly GPUTextureView[] _colorViews;
    private readonly GPUTexture? _depthStencilTexture;
    private readonly GPUTextureView? _depthStencilView;
    private readonly GPUTextureView? _depthView;
    private readonly GPUTextureView? _stencilView;

    protected override VulkanDevice Device { get; }

    public override GPUAttachmentLayout AttachmentLayout
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _attachmentLayout;
    }

    public override ReadOnlySpan<GPUTexture> Colors
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _colorTextures;
    }

    public override ReadOnlySpan<GPUTextureView> ColorViews
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _colorViews;
    }

    public override GPUTexture? DepthStencil
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _depthStencilTexture;
    }

    public override GPUTextureView? DepthStencilView
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _depthStencilView;
    }

    public override GPUTextureView? DepthView
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _depthView;
    }

    public override GPUTextureView? StencilView
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _stencilView;
    }

    public override uint Width
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _width;
    }

    public override uint Height
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _height;
    }

    public VulkanExternalFrameBuffer(VulkanDevice device, in ExternalFrameBufferDescriptor descriptor)
        : base(new FrameBufferDescriptor(descriptor.AttachmentLayout, descriptor.Width, descriptor.Height, descriptor.Name))
    {
        Device = device;
        _attachmentLayout = (VulkanAttachmentLayout)descriptor.AttachmentLayout;
        _width = descriptor.Width;
        _height = descriptor.Height;

        _colorTextures = descriptor.Colors;
        _colorViews = descriptor.ColorViews;
        _depthStencilTexture = descriptor.DepthStencil;
        _depthStencilView = descriptor.DepthStencilView;
        _depthView = descriptor.DepthView;
        _stencilView = descriptor.StencilView;
    }

    protected override void Dispose(bool disposing)
    {
        // externally owned resources are not disposed here
    }
}
