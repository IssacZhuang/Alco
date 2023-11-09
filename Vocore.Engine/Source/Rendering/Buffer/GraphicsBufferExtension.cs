using System;
using Veldrid;

namespace Vocore.Engine
{
    public static class GraphicsBufferExtension
    {
        public static void UpdateBuffer(this CommandList commandList, IGraphicsBuffer buffer)
        {
            buffer.UpdateToGPU(commandList);
        }
    }
}