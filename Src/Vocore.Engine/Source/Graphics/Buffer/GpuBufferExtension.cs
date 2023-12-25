using System;
using System.Runtime.CompilerServices;
using Veldrid;

namespace Vocore.Engine
{
    public static class GpuBufferExtension
    {
        /// <summary>
        /// Update the buffer to the GPU.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void UpdateBuffer(this CommandList commandList, IGpuBuffer buffer)
        {
            buffer.UpdateToGPU(commandList);
        }

        /// <summary>
        /// Update the buffer to the GPU and set it to the given slot.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SetBuffer<T>(this CommandList commandList, uint slot, T buffer)where T: IGpuBuffer, IGpuResource
        {
            buffer.UpdateToGPU(commandList);
            commandList.SetGraphicsResourceSet(slot, buffer.ResourceSet);
        }

        /// <summary>
        /// Set the resource to the given slot.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SetResource<T>(this CommandList commandList, uint slot, T resource) where T : IGpuResource
        {
            commandList.SetGraphicsResourceSet(slot, resource.ResourceSet);
        }
    }
}