using System;
using Vortice.Vulkan;

using static Vortice.Vulkan.Vulkan;

namespace Vocore.Graphics.Vulkan;

internal unsafe static partial class UtilsVulkan
{
    public static VkImageUsageFlags ConvertTextureUsage(TextureUsage usage)
    {
        VkImageUsageFlags flags = VkImageUsageFlags.None;
        if ((usage & TextureUsage.Read) != 0)
        {
            flags |= VkImageUsageFlags.TransferSrc;
        }

        if ((usage & TextureUsage.Write) != 0)
        {
            flags |= VkImageUsageFlags.TransferDst;
        }

        if ((usage & TextureUsage.TextureBinding) != 0)
        {
            flags |= VkImageUsageFlags.Sampled;
        }

        if ((usage & TextureUsage.StorageBinding) != 0)
        {
            flags |= VkImageUsageFlags.Storage;
        }

        if ((usage & TextureUsage.ColorAttachment) != 0)
        {
            flags |= VkImageUsageFlags.ColorAttachment;
        }

        if ((usage & TextureUsage.DepthAttachment) != 0)
        {
            flags |= VkImageUsageFlags.DepthStencilAttachment;
        }

        return flags;
    }

    public static VkBufferUsageFlags ConvertBufferUsage(BufferUsage usage)
    {
        VkBufferUsageFlags flags = VkBufferUsageFlags.None;
        if ((usage & BufferUsage.CopySrc) != 0)
        {
            flags |= VkBufferUsageFlags.TransferSrc;
        }

        if ((usage & BufferUsage.CopyDst) != 0)
        {
            flags |= VkBufferUsageFlags.TransferDst;
        }

        if ((usage & BufferUsage.Uniform) != 0)
        {
            flags |= VkBufferUsageFlags.UniformBuffer;
        }

        if ((usage & BufferUsage.Storage) != 0)
        {
            flags |= VkBufferUsageFlags.StorageBuffer;
        }

        if ((usage & BufferUsage.Index) != 0)
        {
            flags |= VkBufferUsageFlags.IndexBuffer;
        }

        if ((usage & BufferUsage.Vertex) != 0)
        {
            flags |= VkBufferUsageFlags.VertexBuffer;
        }

        if ((usage & BufferUsage.Indirect) != 0)
        {
            flags |= VkBufferUsageFlags.IndirectBuffer;
        }

        return flags;
    }

    public static VkShaderStageFlags ConvertShaderStages(ShaderStage stage)
    {
        VkShaderStageFlags flags = VkShaderStageFlags.None;
        if ((stage & ShaderStage.Vertex) != 0)
        {
            flags |= VkShaderStageFlags.Vertex;
        }

        if ((stage & ShaderStage.Hull) != 0)
        {
            flags |= VkShaderStageFlags.TessellationControl;
        }

        if ((stage & ShaderStage.Domain) != 0)
        {
            flags |= VkShaderStageFlags.TessellationEvaluation;
        }

        if ((stage & ShaderStage.Geometry) != 0)
        {
            flags |= VkShaderStageFlags.Geometry;
        }

        if ((stage & ShaderStage.Fragment) != 0)
        {
            flags |= VkShaderStageFlags.Fragment;
        }

        if ((stage & ShaderStage.Compute) != 0)
        {
            flags |= VkShaderStageFlags.Compute;
        }

        return flags;
    }
}