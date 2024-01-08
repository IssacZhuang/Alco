namespace Vocore.Graphics;


public abstract class GPUDevice : BaseGPUObject
{
    public GPUBuffer CreateBuffer(in GPUBufferDescriptor descriptor)
    {
        return InternalCreateBuffer(descriptor);
    }

    protected abstract GPUBuffer InternalCreateBuffer(in GPUBufferDescriptor descriptor);
}