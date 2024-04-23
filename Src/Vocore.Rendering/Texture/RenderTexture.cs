using System.Runtime.CompilerServices;
using Vocore.Graphics;

namespace Vocore.Rendering;

/// <summary>
/// The high level encapsulation of a GPUFrameBuffer with its entries of GPUTextureView
/// </summary>
public class RenderTexture : ShaderResource
{
    private readonly GPUDevice _device;
    private readonly GPUFrameBuffer _frameBuffer;
    private readonly GPUResourceGroup[] _groupsColorRead;
    private readonly GPUResourceGroup[] _groupsColorWrite;
    private readonly GPUResourceGroup? _groupDepthRead;
    private readonly GPUResourceGroup? _groupDepthWrite;

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
    /// <value>The hieght.</value>
    public uint Hieght
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
        get => _groupsColorRead.Length;
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
    public IReadOnlyList<GPUResourceGroup> EntriesRead
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _groupsColorRead;
    }

    /// <summary>
    /// The entries of color view for writing. Usually for write in compute shader.
    /// </summary>
    /// <value></value>
    public IReadOnlyList<GPUResourceGroup> EntriesWrite
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _groupsColorWrite;
    }

    /// <summary>
    /// The entry of depth view for reading. Usually for read in compute shader.
    /// </summary>
    /// <value></value>
    public GPUResourceGroup? EntryDepthRead
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _groupDepthRead;
    }

    /// <summary>
    /// The entry of depth view for writing. Usually for write in compute shader.
    /// </summary>
    /// <value></value>
    public GPUResourceGroup? EntryDepthWrite
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _groupDepthWrite;
    }

    internal RenderTexture(
        GPUDevice device,
        GPUFrameBuffer frameBuffer,
        GPUResourceGroup[] groupColorRead,
        GPUResourceGroup[] groupColorWrite,
        GPUResourceGroup? groupDepthRead,
        GPUResourceGroup? groupDepthWrite)
    {
        _device = device;
        _frameBuffer = frameBuffer;
        _groupsColorRead = groupColorRead;
        _groupsColorWrite = groupColorWrite;
        _groupDepthRead = groupDepthRead;
        _groupDepthWrite = groupDepthWrite;
    }

    protected override void Dispose(bool disposing)
    {
        _groupDepthRead?.Dispose();
        _groupDepthWrite?.Dispose();

        for (int i = 0; i < ColorCount; i++)
        {
            _groupsColorRead[i].Dispose();
            _groupsColorWrite[i].Dispose();
        }

        _frameBuffer.Dispose();
    }
}