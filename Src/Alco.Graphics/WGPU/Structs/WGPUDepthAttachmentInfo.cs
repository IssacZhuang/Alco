using WebGPU;

namespace Alco.Graphics.WebGPU;

internal struct WGPUDepthAttachmentInfo
{
    public WGPUTextureFormat format;
    public float clearDepth;
    public bool isDepthReadOnly;
    public uint clearStencil;
    public bool isStencilReadOnly;
}