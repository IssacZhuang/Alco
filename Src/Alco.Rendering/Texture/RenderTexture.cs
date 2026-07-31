using System.Runtime.CompilerServices;
using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// The high level encapsulation of a GPUFrameBuffer with its entries of GPUTextureView
/// </summary>
public sealed class RenderTexture : AutoDisposable
{
    private readonly GPUDevice _device;
    private readonly GPUSampler _sampler;
    private readonly GPUFrameBuffer _frameBuffer;
    private GPUResourceGroup? _groupDepthSample;
    private GPUResourceGroup? _groupDepthComparison;
    private readonly Texture2D[] _colorTextures;

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
        _device = renderingSystem.GraphicsDevice;
        _frameBuffer = frameBuffer;
        _sampler = sampler;

        _colorTextures = new Texture2D[_frameBuffer.Colors.Length];
        for (int i = 0; i < _colorTextures.Length; i++)
        {
            _colorTextures[i] = renderingSystem.CreateTexture2D(
                _frameBuffer.Colors[i],
                _frameBuffer.ColorViews[i],
                _sampler
                );
        }
    }

    private GPUResourceGroup CreateGroupDepthRead(GPUTextureView view)
    {
        ResourceGroupDescriptor groupDescriptor = new ResourceGroupDescriptor(
            _device.BindGroupTextureDepthRead,
            new ResourceBindingEntry[]{
                new ResourceBindingEntry(0, view),
            }
        );

        return _device.CreateResourceGroup(groupDescriptor);
    }

    private GPUResourceGroup CreateGroupDepthComparison(GPUTextureView view)
    {
        ResourceGroupDescriptor groupDescriptor = new ResourceGroupDescriptor(
            _device.BindGroupTextureDepthComparison,
            new ResourceBindingEntry[]{
                new ResourceBindingEntry(0, view),
                new ResourceBindingEntry(1, _device.SamplerDepthComparison),
            }
        );

        return _device.CreateResourceGroup(groupDescriptor);
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
            _frameBuffer.Dispose();
        }
    }
}