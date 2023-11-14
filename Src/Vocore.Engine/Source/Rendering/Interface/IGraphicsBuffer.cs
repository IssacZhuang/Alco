using System;
using Veldrid;

namespace Vocore.Engine
{
    public interface IGraphicsBuffer
    {
        DeviceBuffer Buffer { get; }
        /// <summary>
        /// Update the buffer from memory to GPU.<br/>
        /// Should be called after the command list started, and before the command list ended.
        /// </summary>
        void UpdateToGPU(CommandList commandList);
    }
}