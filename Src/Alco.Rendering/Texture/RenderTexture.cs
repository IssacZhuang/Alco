using System.Runtime.CompilerServices;
using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// The high level encapsulation of a GPUFrameBuffer with its entries of GPUTextureView.
/// <br/>The wrapper identity is stable across <see cref="Resize"/>: the internal GPU
/// resources (frame buffer, textures, views) are recreated in place, so materials and
/// render nodes referencing this object never need to be rebound after a resize.
/// </summary>
public sealed class RenderTexture : AutoDisposable
{
    private readonly RenderingSystem _rendering;
    private readonly GPUSampler _sampler;
    private GPUFrameBuffer _frameBuffer;
    private GPUAttachmentLayout? _colorOnlyLayout;
    private GPUFrameBuffer? _colorOnlyFrameBuffer;
    private GPUResourceGroup? _groupDepthSample;
    private GPUResourceGroup? _groupDepthComparison;
    private readonly Texture2D[] _colorTextures;
    private uint _version;

    /// <summary>
    /// The internal GPUFrameBuffer object.
    /// </summary>
    /// <value></value>
    public GPUFrameBuffer FrameBuffer
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _frameBuffer;
    }

    /// <summary>
    /// A framebuffer over the same color attachments without the depth attachment.
    /// Full-screen passes that sample this render texture's depth must render through
    /// this view to avoid binding one depth texture as both an attachment and a sampled
    /// resource in the same pass.
    /// </summary>
    public GPUFrameBuffer ColorFrameBuffer
    {
        get
        {
            if (!HasDepth)
            {
                return _frameBuffer;
            }

            if (_colorOnlyFrameBuffer == null)
            {
                _colorOnlyLayout ??= _rendering.GraphicsDevice.CreateAttachmentLayout(
                    new AttachmentLayoutDescriptor(
                        _frameBuffer.AttachmentLayout.Colors.ToArray(),
                        null,
                        _frameBuffer.Name + "_color_layout"));

                _colorOnlyFrameBuffer = _rendering.GraphicsDevice.CreateExternalFrameBuffer(
                    new ExternalFrameBufferDescriptor(
                        _colorOnlyLayout,
                        _frameBuffer.Colors.ToArray(),
                        _frameBuffer.ColorViews.ToArray(),
                        Width,
                        Height,
                        _frameBuffer.Name + "_color_framebuffer"));
            }

            return _colorOnlyFrameBuffer;
        }
    }

    /// <summary>
    /// The width of the frame buffer.
    /// </summary>
    /// <value>The width.</value>
    public uint Width
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _frameBuffer.Width;
    }

    /// <summary>
    /// The height of the frame buffer.
    /// </summary>
    /// <value>The height.</value>
    public uint Height
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _frameBuffer.Height;
    }

    /// <summary>
    /// The count of the color attachments in frame buffer. Also the count of the entris of color view .
    /// </summary>
    /// <value>The color count.</value>
    public int ColorCount
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _frameBuffer.Colors.Length;
    }

    /// <summary>
    /// If the frame buffer has depth attachment.
    /// </summary>
    /// <value><c>true</c> if has depth; otherwise, <c>false</c>.</value>
    public bool HasDepth
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _frameBuffer.DepthStencil != null;
    }

    /// <summary>
    /// The name of the render texture.
    /// </summary>
    public string Name
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _frameBuffer.Name;
    }


    /// <summary>
    /// The entry of depth view for sampling.
    /// </summary>
    /// <value></value>
    public GPUResourceGroup? EntryDepthRead
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if (!HasDepth)
            {
                return null;
            }

            if (_groupDepthSample == null)
            {
                _groupDepthSample = CreateGroupDepthRead(_frameBuffer.DepthView!);
            }

            return _groupDepthSample;
        }
    }

    /// <summary>
    /// The entry of depth view and comparison sampler for depth comparison sampling
    /// (e.g. shadow map PCF).
    /// </summary>
    /// <value></value>
    public GPUResourceGroup? EntryDepthComparison
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if (!HasDepth)
            {
                return null;
            }

            if (_groupDepthComparison == null)
            {
                _groupDepthComparison = CreateGroupDepthComparison(_frameBuffer.DepthView!);
            }

            return _groupDepthComparison;
        }
    }

    /// <summary>
    /// The depth texture view of the depth attachment, or null when the render texture
    /// has no depth attachment.
    /// </summary>
    public GPUTextureView? DepthView
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _frameBuffer.DepthView;
    }

    /// <summary>
    /// The color textures
    /// </summary>
    /// <value></value>
    public Span<Texture2D> ColorTextures
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _colorTextures;
    }

    /// <summary>
    /// The attachment layout of the frame buffer.
    /// </summary>
    public GPUAttachmentLayout AttachmentLayout
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _frameBuffer.AttachmentLayout;
    }


    internal RenderTexture(
        RenderingSystem renderingSystem,
        GPUFrameBuffer frameBuffer,
        GPUSampler sampler
        )
    {
        _rendering = renderingSystem;
        _frameBuffer = frameBuffer;
        _sampler = sampler;

        _colorTextures = new Texture2D[_frameBuffer.Colors.Length];
        for (int i = 0; i < _colorTextures.Length; i++)
        {
            // Non-owning wrappers: the attachments belong to the frame buffer
            // (for external frame buffers, to the render graph's texture pool);
            // disposing them here only releases the wrapper's own bind groups.
            _colorTextures[i] = renderingSystem.CreateTexture2D(
                _frameBuffer.Colors[i],
                _frameBuffer.ColorViews[i],
                _sampler
                );
        }
    }

    /// <summary>
    /// The content version of the render texture, incremented by every
    /// <see cref="Resize"/>. The material system compares the version recorded at bind
    /// time against the current one to detect the recreated GPU resources and rebuild
    /// the affected bind groups automatically. The value wraps around on overflow: it
    /// is only ever compared for equality.
    /// </summary>
    public uint Version
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _version;
    }

    /// <summary>
    /// Recreates the internal GPU resources at a new size in place. The wrapper identity
    /// (and therefore every material and render node referencing it) stays valid; the
    /// affected bind groups are rebuilt automatically on next use through the
    /// <see cref="Version"/> check of the material system. A call with the current size
    /// is a no-op.
    /// </summary>
    /// <param name="width">The new width in pixels.</param>
    /// <param name="height">The new height in pixels.</param>
    /// <exception cref="ObjectDisposedException">The render texture has been disposed.</exception>
    public void Resize(uint width, uint height)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        if (width == Width && height == Height)
        {
            return;
        }

        // Capture the recreation inputs before releasing the current frame buffer:
        // the attachment layout is a standalone object and outlives it.
        GPUAttachmentLayout layout = _frameBuffer.AttachmentLayout;
        string name = _frameBuffer.Name;

        ReplaceFrameBuffer(_rendering.GraphicsDevice.CreateFrameBuffer(new FrameBufferDescriptor(layout, width, height, name)));
    }

    /// <summary>
    /// Rebinds the wrapper to a different frame buffer in place. Used by
    /// <see cref="RenderGraph"/> to swap the pooled backing of a transient resource:
    /// the wrapper identity stays valid, the affected bind groups are rebuilt
    /// automatically on next use through the <see cref="Version"/> check.
    /// <br/>Ownership of <paramref name="frameBuffer"/> transfers to the wrapper (it
    /// is disposed on the next rebind or on <see cref="Dispose"/>); for external
    /// frame buffers disposal only releases the composed descriptor, never the pooled
    /// textures.
    /// </summary>
    /// <param name="frameBuffer">The new backing frame buffer.</param>
    internal void Rebind(GPUFrameBuffer frameBuffer)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        ArgumentNullException.ThrowIfNull(frameBuffer);

        ReplaceFrameBuffer(frameBuffer);
    }

    /// <summary>
    /// Swaps the backing frame buffer: releases the depth sample groups and color
    /// texture wrappers of the old one, disposes it (deferred destruction keeps
    /// in-flight GPU work valid), wraps the new one's attachments and bumps
    /// <see cref="Version"/>.
    /// </summary>
    private void ReplaceFrameBuffer(GPUFrameBuffer frameBuffer)
    {
        _colorOnlyFrameBuffer?.Dispose();
        _colorOnlyFrameBuffer = null;

        // The cached depth sample groups reference the old depth view; they are
        // recreated lazily from the new frame buffer on next access.
        _groupDepthSample?.Dispose();
        _groupDepthSample = null;
        _groupDepthComparison?.Dispose();
        _groupDepthComparison = null;

        for (int i = 0; i < _colorTextures.Length; i++)
        {
            _colorTextures[i].Dispose();
        }

        _frameBuffer.Dispose();
        _frameBuffer = frameBuffer;

        for (int i = 0; i < _colorTextures.Length; i++)
        {
            _colorTextures[i] = _rendering.CreateTexture2D(
                _frameBuffer.Colors[i],
                _frameBuffer.ColorViews[i],
                _sampler
                );
        }

        // Wraps around on overflow instead of throwing: the version is only ever
        // compared for equality.
        unchecked
        {
            _version++;
        }
    }

    private GPUResourceGroup CreateGroupDepthRead(GPUTextureView view)
    {
        ResourceGroupDescriptor groupDescriptor = new ResourceGroupDescriptor(
            _rendering.GraphicsDevice.BindGroupTextureDepthRead,
            new ResourceBindingEntry[]{
                new ResourceBindingEntry(0, view),
            }
        );

        return _rendering.GraphicsDevice.CreateResourceGroup(groupDescriptor);
    }

    private GPUResourceGroup CreateGroupDepthComparison(GPUTextureView view)
    {
        ResourceGroupDescriptor groupDescriptor = new ResourceGroupDescriptor(
            _rendering.GraphicsDevice.BindGroupTextureDepthComparison,
            new ResourceBindingEntry[]{
                new ResourceBindingEntry(0, view),
                new ResourceBindingEntry(1, _rendering.GraphicsDevice.SamplerDepthComparison),
            }
        );

        return _rendering.GraphicsDevice.CreateResourceGroup(groupDescriptor);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            //dispose managed resources
            foreach (var texture in _colorTextures)
            {
                texture.Dispose();
            }

            _groupDepthSample?.Dispose();
            _groupDepthComparison?.Dispose();
            _colorOnlyFrameBuffer?.Dispose();
            _colorOnlyLayout?.Dispose();
            _frameBuffer.Dispose();
        }
    }
}
