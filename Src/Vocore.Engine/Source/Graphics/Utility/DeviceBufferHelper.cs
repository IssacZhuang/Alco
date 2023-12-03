using System;
using System.Collections.Generic;
using Veldrid;
using Vocore.Unsafe;

namespace Vocore.Engine
{
    public static class DeviceBufferHelper
    {
        public static uint GetUniformBufferSize<T>() where T : unmanaged
        {
            return GetUniformBufferSize(UtilsMemory.SizeOf<T>());
        }

        public static uint GetUniformBufferSize(int size)
        {
            uint sizeInBytes = (uint)size;
            uint remainder = sizeInBytes % 16;
            //Uniform buffer size must be a multiple of 16 bytes.
            sizeInBytes += remainder == 0 ? 0 : 16 - remainder;
            return sizeInBytes;
        }

        public static ResourceLayout CreateTextureLayout(this GraphicsDevice device, ShaderStages usage = ShaderStages.Fragment)
        {
            return device.ResourceFactory.CreateTextureLayout(usage);
        }

        public static ResourceLayout CreateTextureLayout(this ResourceFactory factory, ShaderStages usage = ShaderStages.Fragment)
        {
            return factory.CreateResourceLayout(
                new ResourceLayoutDescription(
                    new ResourceLayoutElementDescription("Texture", ResourceKind.TextureReadOnly, usage),
                    new ResourceLayoutElementDescription("Sampler", ResourceKind.Sampler, usage)
                )
            );
        }

        public static bool IsBufferUsageValidate(BufferUsage usage, ResourceKind forResourceKind)
        {
            switch (forResourceKind)
            {
                case ResourceKind.UniformBuffer:
                    return usage == BufferUsage.UniformBuffer;
                case ResourceKind.StructuredBufferReadOnly:
                    return usage == BufferUsage.StructuredBufferReadOnly || usage == BufferUsage.StructuredBufferReadWrite;
                case ResourceKind.StructuredBufferReadWrite:
                    return usage == BufferUsage.StructuredBufferReadWrite;
                default:
                    return false;
            }
        }
    }
}

