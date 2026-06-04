using WebGPU;

namespace Alco.Graphics.WebGPU;

internal struct WGPUColorAttachmentInfo
{
    public WGPUTextureFormat format;
    public WGPUColor clearColor;
}