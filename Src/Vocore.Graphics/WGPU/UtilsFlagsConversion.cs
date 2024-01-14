using WebGPU;
using static WebGPU.WebGPU;

namespace Vocore.Graphics.WebGPU;

public static partial class UtilsWebGPU
{
    public static WGPUTextureUsage ConvertTextureUsage(TextureUsage usage)
    {
        WGPUTextureUsage result = 0;
        if ((usage & TextureUsage.Read) != 0)
        {
            result |= WGPUTextureUsage.CopySrc;
        }
        if ((usage & TextureUsage.Write) != 0)
        {
            result |= WGPUTextureUsage.CopyDst;
        }
        if ((usage & TextureUsage.TextureBinding) != 0)
        {
            result |= WGPUTextureUsage.TextureBinding;
        }
        if ((usage & TextureUsage.StorageBinding) != 0)
        {
            result |= WGPUTextureUsage.StorageBinding;
        }
        if ((usage & TextureUsage.RenderAttachment) != 0)
        {
            result |= WGPUTextureUsage.RenderAttachment;
        }
        return result;
    }

}