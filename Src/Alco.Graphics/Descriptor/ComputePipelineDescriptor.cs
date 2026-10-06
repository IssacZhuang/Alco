namespace Alco.Graphics
{
    /// <summary>
    /// The creation information for a compute pipeline: the compute shader, its
    /// bind group layouts and the push constants size.
    /// </summary>
    public struct ComputePipelineDescriptor
    {
        /// <summary>
        /// Initializes the descriptor with the compute shader and its bind groups.
        /// </summary>
        public ComputePipelineDescriptor(
            ShaderModule computeShader,
            GPUBindGroup[] bindGroups,
            uint pushConstantsSize = 0,
            string name = "unnamed_compute_pipeline")
        {
            Name = name;
            BindGroups = bindGroups;
            Source = computeShader;
            PushConstantsSize = pushConstantsSize;
        }

        /// <summary>
        /// The compute <see cref="ShaderModule"/> the pipeline runs.
        /// </summary>
        public ShaderModule Source { get; init; }
        /// <summary>
        /// The bind group layouts the pipeline was created with; resource groups
        /// used with it must match these layouts.
        /// </summary>
        public GPUBindGroup[] BindGroups { get; init; }
        /// <summary>
        /// Total size in bytes of the push constants (immediates) block used by the shader, 0 when unused.
        /// </summary>
        public uint PushConstantsSize { get; init; }
        /// <summary>
        /// Diagnostic name shown in errors and debuggers.
        /// </summary>
        public string Name { get; init; } = "unnamed_compute_pipeline";


    }
}