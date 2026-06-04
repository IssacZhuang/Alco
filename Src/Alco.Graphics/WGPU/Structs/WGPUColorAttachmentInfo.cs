using Alco.Graphics.WebGPU.Bindings;

namespace Alco.Graphics.WebGPU;

internal struct WGPUColorAttachmentInfo
{
    public WGPUTextureFormat format;
    public WGPUColor clearColor;
}