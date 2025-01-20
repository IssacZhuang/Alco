using System.Runtime.CompilerServices;
using Vocore.Graphics;

namespace Vocore.Rendering;

/// <summary>
/// The high level encapsulation of a GPUFrameBuffer with its entries of GPUTextureView
/// </summary>
public class RenderTexture : AutoDisposable
{
    private readonly GPUDevice _device;
    private readonly GPUSampler _sampler;
    private readonly GPUFrameBuffer _frameBuffer;
    private GPUResourceGroup? _groupDepthSample;
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
        get => _frameBuffer.Depth != null;
    }


    /// <summary>
    /// The entry of depth view for sampling.
    /// </summary>
    /// <value></value>
    public GPUResourceGroup? EntryDepthSample
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
                _groupDepthSample = CreateGroupSample(_frameBuffer.DepthView!);
            }

            return _groupDepthSample;
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
    /// The render pass of the frame buffer.
    /// </summary>
    public GPURenderPass RenderPass
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _frameBuffer.RenderPass;
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

    private GPUResourceGroup CreateGroupSample(GPUTextureView view)
    {
        ResourceGroupDescriptor groupDescriptor = new ResourceGroupDescriptor(
            _device.BindGroupTexture2DSampled,
            new ResourceBindingEntry[]{
                new ResourceBindingEntry(0, view),
                new ResourceBindingEntry(1, _sampler)
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
            _frameBuffer.Dispose();
        }
    }
}