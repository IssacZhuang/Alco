using System;
using Veldrid;

namespace Vocore.Engine
{
    public interface IGraphicsBuffer
    {
        void ApplyToGPU(CommandList commandList);
    }
}