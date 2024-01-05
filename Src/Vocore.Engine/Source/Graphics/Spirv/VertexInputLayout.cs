using System;
using Veldrid;

namespace Vocore.Engine
{
    public struct VertexInputLayout
    {
        
        public VertexElement[] Elements;
        public uint Stride;
        public VertexStepMode StepMode;

        public VertexInputLayout(VertexElement[] elements, uint stride, VertexStepMode stepMode)
        {
            Elements = elements;
            Stride = stride;
            StepMode = stepMode;
        }
    }
}