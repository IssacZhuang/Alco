using System.Runtime.CompilerServices;
using WebGPU;
using static WebGPU.WebGPU;

namespace Vocore.Graphics.WebGPU;

internal class WebGPUBuffer : GPUBuffer
{
    #region Properties
    private readonly WGPUBuffer _buffer;
    private readonly uint _size;
    private readonly BufferUsage _usage;

    #endregion

    #region Abstract Implementation
    public override uint Size
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _size;
    }

    public override BufferUsage Usage
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _usage;
    }

    public override BindableResourceType ResourceType
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => BindableResourceType.Buffer;
    }

    public override string Name { get; }

    protected override void Dispose(bool disposing)
    {
        wgpuBufferDestroy(_buffer);
        wgpuBufferRelease(_buffer);
    }

    #endregion

    #region WebGPU Implementation

    public WGPUBuffer Native
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _buffer;
    }

    public unsafe WebGPUBuffer(WGPUDevice nativeDevice, in BufferDescriptor descriptor)
    {
        Name = descriptor.Name;
        _size = UtilsBuffer.GetBufferSize(descriptor.Size);
        _usage = descriptor.Usage;
        
        fixed (sbyte* name = descriptor.Name.GetUtf8Span())
        {
            WGPUBufferDescriptor bufferDescriptor = new()
            {
                nextInChain = null,
                label = name,
                size = descriptor.Size,
                usage = UtilsWebGPU.ConvertBufferUsage(descriptor.Usage),
                mappedAtCreation = false,
            };

            _buffer = wgpuDeviceCreateBuffer(nativeDevice, &bufferDescriptor);
        }
    }

    #endregion
}