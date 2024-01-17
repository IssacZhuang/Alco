using System.Runtime.CompilerServices;
using WebGPU;
using static WebGPU.WebGPU;

namespace Vocore.Graphics.WebGPU;

internal class WebGPUBuffer : GPUBuffer
{
    private readonly WGPUBuffer _buffer;
    public WGPUBuffer Native
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _buffer;
    }
    public override uint Size => throw new NotImplementedException();

    public override BufferUsage Usage => throw new NotImplementedException();

    public override string Name { get; }

    public unsafe WebGPUBuffer(WebGPUDevice device, in BufferDescriptor descriptor)
    {
        Name = descriptor.Name;
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

            _buffer = wgpuDeviceCreateBuffer(device.Native, &bufferDescriptor);
        }
    }

    protected override void Dispose(bool disposing)
    {
        throw new NotImplementedException();
    }
}