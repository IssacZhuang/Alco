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

    public static WGPUBufferUsage ConvertBufferUsage(BufferUsage usage)
    {
        WGPUBufferUsage result = 0;

        if ((usage & BufferUsage.MapRead) != 0)
        {
            result |= WGPUBufferUsage.MapRead;
        }
        if ((usage & BufferUsage.MapWrite) != 0)
        {
            result |= WGPUBufferUsage.MapWrite;
        }
        if ((usage & BufferUsage.CopySrc) != 0)
        {
            result |= WGPUBufferUsage.CopySrc;
        }
        if ((usage & BufferUsage.CopyDst) != 0)
        {
            result |= WGPUBufferUsage.CopyDst;
        }
        if ((usage & BufferUsage.Uniform) != 0)
        {
            result |= WGPUBufferUsage.Uniform;
        }
        if ((usage & BufferUsage.Storage) != 0)
        {
            result |= WGPUBufferUsage.Storage;
        }
        if ((usage & BufferUsage.Index) != 0)
        {
            result |= WGPUBufferUsage.Index;
        }
        if ((usage & BufferUsage.Vertex) != 0)
        {
            result |= WGPUBufferUsage.Vertex;
        }
        if ((usage & BufferUsage.Indirect) != 0)
        {
            result |= WGPUBufferUsage.Indirect;
        }
        return result;
    }

}