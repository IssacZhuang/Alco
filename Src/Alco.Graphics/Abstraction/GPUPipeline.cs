using System.Runtime.CompilerServices;

namespace Alco.Graphics
{
    /// <summary>
    /// A configured shader program plus fixed-function state, ready for dispatch in a pass.
    /// </summary>
    public abstract class GPUPipeline : BaseGPUObject
    {
        /// <summary>
        /// The shader stages that this pipeline is using. This is a flag enum which can contain multiple stages
        /// </summary>
        /// <value>The shader stages</value>
        public ShaderStage Stages { get; }

        /// <summary>
        /// Gets whether this pipeline was created for compute (as opposed to graphics).
        /// </summary>
        public bool IsComputePipeline
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (Stages & ShaderStage.Compute) != 0;
        }

        protected GPUPipeline(in GraphicsPipelineDescriptor descriptor): base(descriptor.Name)
        {
            ShaderStage stages = ShaderStage.None;
            for (int i = 0; i < descriptor.ShaderModules.Length; i++)
            {
                stages |= descriptor.ShaderModules[i].Stage;
            }
            Stages = stages;


            if (!DescriptorUtility.IsGraphicsShader(descriptor.ShaderModules))
                throw new GraphicsException("The shader stages must contain a vertex and a pixel shader when creating a graphics pipeline");
        }

        protected GPUPipeline(in ComputePipelineDescriptor descriptor): base(descriptor.Name)
        {
            if (descriptor.Source.Stage != ShaderStage.Compute)
                throw new GraphicsException("The shader stages must contain a compute shader when creating a compute pipeline");
        }
    }
}