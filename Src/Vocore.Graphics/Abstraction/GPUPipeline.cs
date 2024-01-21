using System.Runtime.CompilerServices;

namespace Vocore.Graphics
{
    public abstract class GPUPipeline : BaseGPUObject
    {
        public abstract ShaderStage Stages { get; }

        public bool IsComputePipeline
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (Stages & ShaderStage.Compute) != 0;
        }
    }
}