using WebGPU;

namespace Vocore.Graphics.WebGPU;

internal struct WGPUColorAttachmentInfo
{
    public WGPUTextureFormat format;
    public WGPUColor clearColor;
}