namespace Alco.Graphics
{
    public struct ComputePipelineDescriptor
    {
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

        public ShaderModule Source { get; init; }
        public GPUBindGroup[] BindGroups { get; init; }
        /// <summary>
        /// Total size in bytes of the push constants (immediates) block used by the shader, 0 when unused.
        /// </summary>
        public uint PushConstantsSize { get; init; }
        public string Name { get; init; } = "unnamed_compute_pipeline";


    }
}