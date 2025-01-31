using WebGPU;
using static WebGPU.WebGPU;

namespace Alco.Graphics.WebGPU;

internal static partial class UtilsWebGPU
{
    public static WGPUStorageTextureAccess ConvertAccessMode(AccessMode access)
    {
        switch (access)
        {
            case AccessMode.None:
                return WGPUStorageTextureAccess.Undefined;
            case AccessMode.Read:
                return WGPUStorageTextureAccess.ReadOnly;
            case AccessMode.Write:
                return WGPUStorageTextureAccess.WriteOnly;
            case AccessMode.ReadWrite:
                return WGPUStorageTextureAccess.ReadWrite;
            default:
                throw new ArgumentException($"Invalid access mode: {access}");
        }
    }


    public static WGPUTextureUsage ConvertTextureUsage(TextureUsage usage)
    {
        WGPUTextureUsage result = WGPUTextureUsage.None;
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
        if ((usage & TextureUsage.ColorAttachment) != 0)
        {
            result |= WGPUTextureUsage.RenderAttachment;
        }
        if ((usage & TextureUsage.DepthAttachment) != 0)
        {
            result |= WGPUTextureUsage.RenderAttachment;
        }

        return result;
    }

    public static WGPUBufferUsage ConvertBufferUsage(BufferUsage usage)
    {
        WGPUBufferUsage result = WGPUBufferUsage.None;

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

    public static WGPUShaderStage ConvertShaderStage(ShaderStage stage)
    {
        WGPUShaderStage result = WGPUShaderStage.None;
        if ((stage & ShaderStage.Vertex) != 0)
        {
            result |= WGPUShaderStage.Vertex;
        }

        if ((stage & ShaderStage.Fragment) != 0)
        {
            result |= WGPUShaderStage.Fragment;
        }

        if ((stage & ShaderStage.Compute) != 0)
        {
            result |= WGPUShaderStage.Compute;
        }

        return result;
    }

}