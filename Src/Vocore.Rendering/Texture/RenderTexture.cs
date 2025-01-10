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
    private GPUResourceGroup[]? _groupsColorRead;
    private GPUResourceGroup[]? _groupsColorWrite;
    private GPUResourceGroup[]? _groupsColorSample;
    private GPUResourceGroup? _groupDepthSample;

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
    /// The entries of color view for reading. Usually for read in compute shader.
    /// </summary>
    /// <value></value>
    public ReadOnlySpan<GPUResourceGroup> EntriesColorRead
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if (_groupsColorRead == null)
            {
                _groupsColorRead = CreateGroupsColorRead();
            }
            return _groupsColorRead;
        }
    }

    /// <summary>
    /// The entries of color view for writing. Usually for write in compute shader.
    /// </summary>
    /// <value></value>
    public ReadOnlySpan<GPUResourceGroup> EntriesColorWrite
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if (_groupsColorWrite == null)
            {
                _groupsColorWrite = CreateGroupsColorWrite();
            }
            return _groupsColorWrite;
        }
    }

    /// <summary>
    /// The entries of color view for sampling.
    /// </summary>
    /// <value></value>
    public ReadOnlySpan<GPUResourceGroup> EntriesColorSample
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if (_groupsColorSample == null)
            {
                _groupsColorSample = CreateGroupsColorSample();
            }
            return _groupsColorSample;
        }
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
    /// The render pass of the frame buffer.
    /// </summary>
    public GPURenderPass RenderPass
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _frameBuffer.RenderPass;
    }


    internal RenderTexture(
        GPUDevice device,
        GPUFrameBuffer frameBuffer,
        GPUSampler sampler
        )
    {
        _device = device;
        _frameBuffer = frameBuffer;
        _sampler = sampler;
    }

    private GPUResourceGroup[] CreateGroupsColorSample()
    {
        GPUResourceGroup[] groups = new GPUResourceGroup[_frameBuffer.Colors.Length];
        for (int i = 0; i < groups.Length; i++)
        {
            groups[i] = CreateGroupSample(_frameBuffer.ColorViews[i]);
        }
        return groups;
    }

    private GPUResourceGroup[] CreateGroupsColorRead()
    {
        GPUResourceGroup[] groups = new GPUResourceGroup[_frameBuffer.Colors.Length];
        for (int i = 0; i < groups.Length; i++)
        {
            groups[i] = CreateGroupRead(_frameBuffer.ColorViews[i]);
        }
        return groups;
    }

    private GPUResourceGroup[] CreateGroupsColorWrite()
    {
        GPUResourceGroup[] groups = new GPUResourceGroup[_frameBuffer.Colors.Length];
        for (int i = 0; i < groups.Length; i++)
        {
            groups[i] = CreateGroupWrite(_frameBuffer.ColorViews[i]);
        }
        return groups;
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

    private GPUResourceGroup CreateGroupRead(GPUTextureView view)
    {
        ResourceGroupDescriptor groupDescriptor = new ResourceGroupDescriptor(
            _device.BindGroupTexture2DRead,
            new ResourceBindingEntry[]{
                new ResourceBindingEntry(0, view)
            }
        );

        return _device.CreateResourceGroup(groupDescriptor);
    }

    private GPUResourceGroup CreateGroupWrite(GPUTextureView view)
    {
        ResourceGroupDescriptor groupDescriptor = new ResourceGroupDescriptor(
            _device.BindGroupTexture2DStorage,
            new ResourceBindingEntry[]{
                new ResourceBindingEntry(0, view)
            }
        );

        return _device.CreateResourceGroup(groupDescriptor);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            //dispose managed resources
            if (_groupsColorRead != null)
            {
                for (int i = 0; i < _groupsColorRead.Length; i++)
                {
                    _groupsColorRead[i].Dispose();
                }
            }

            if (_groupsColorWrite != null)
            {
                for (int i = 0; i < _groupsColorWrite.Length; i++)
                {
                    _groupsColorWrite[i].Dispose();
                }
            }

            if (_groupsColorSample != null)
            {
                for (int i = 0; i < _groupsColorSample.Length; i++)
                {
                    _groupsColorSample[i].Dispose();
                }
            }

            _groupDepthSample?.Dispose();
            _frameBuffer.Dispose();
        }
    }
}