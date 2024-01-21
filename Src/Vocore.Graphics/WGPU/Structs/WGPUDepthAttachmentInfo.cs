using WebGPU;

namespace Vocore.Graphics.WebGPU;

internal struct WGPUDepthAttachmentInfo
{
    public WGPUTextureFormat format;
    public float clearDepth;
    public bool isDepthReadOnly;
    public uint clearStencil;
    public bool isStencilReadOnly;
}