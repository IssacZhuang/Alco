using System.Runtime.CompilerServices;

namespace Vocore.Graphics
{
    public abstract class GPUPipeline : BaseGPUObject
    {
        /// <summary>
        /// The shader stages that this pipeline is using. This is a flag enum which can contain multiple stages
        /// </summary>
        /// <value>The shader stages</value>
        public abstract ShaderStage Stages { get; }

        /// <summary>
        /// Is the pipeline a compute pipeline
        /// </summary>
        /// <value>Is the pipeline a compute pipeline</value>
        public bool IsComputePipeline
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (Stages & ShaderStage.Compute) != 0;
        }
    }
}