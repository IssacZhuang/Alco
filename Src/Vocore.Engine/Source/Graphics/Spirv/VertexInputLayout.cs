using System;
using Veldrid;

namespace Vocore.Engine
{
    public struct VertexInputLayout
    {
        public VertexStepMode StepMode;
        public VertexElement[] Elements;
        public uint Stride;
    }
}