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
    }
}