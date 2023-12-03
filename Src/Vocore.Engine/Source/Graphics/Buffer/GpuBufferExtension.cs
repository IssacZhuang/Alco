using System;
using Veldrid;

namespace Vocore.Engine
{
    public static class GpuBufferExtension
    {
        public static void UpdateBuffer(this CommandList commandList, IGpuBuffer buffer)
        {
            buffer.UpdateToGPU(commandList);
        }

        public static void SetBuffer<T>(this CommandList commandList, uint slot, T buffer)where T: IGpuBuffer, IGpuResource
        {
            buffer.UpdateToGPU(commandList);
            commandList.SetGraphicsResourceSet(slot, buffer.ResourceSet);
        }
    }
}