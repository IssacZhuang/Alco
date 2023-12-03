using System;
using Veldrid;
using System.Collections.Generic;

namespace Vocore.Engine
{
    public static partial class BufferLayout
    {

        public static readonly ResourceLayoutDescription Uniform = new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("Uniform", ResourceKind.UniformBuffer, ShaderStages.Vertex|ShaderStages.Fragment|ShaderStages.Compute)
        );

        public static readonly ResourceLayoutDescription StructuredReadonly = new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("StructuredReadonly", ResourceKind.StructuredBufferReadOnly, ShaderStages.Vertex | ShaderStages.Fragment | ShaderStages.Compute)
        );

        public static readonly ResourceLayoutDescription StructuredReadWrite = new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("StructuredReadWrite", ResourceKind.StructuredBufferReadWrite, ShaderStages.Vertex | ShaderStages.Fragment | ShaderStages.Compute)
        );

        public static readonly ResourceLayoutDescription TextureReadonly = new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("Texture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("Sampler", ResourceKind.Sampler, ShaderStages.Fragment)
        );

        public static readonly ResourceLayoutDescription TextureReadWrite = new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("Texture", ResourceKind.TextureReadWrite, ShaderStages.Fragment | ShaderStages.Compute),
            new ResourceLayoutElementDescription("Sampler", ResourceKind.Sampler, ShaderStages.Fragment | ShaderStages.Compute)
        );


    }
}

