namespace Alco.Graphics
{
    public struct ComputePipelineDescriptor
    {
        public ComputePipelineDescriptor(
            ShaderModule computeShader,
            GPUBindGroup[] bindGroups,
            PushConstantsRange[]? pushConstantsRanges = null,
            string name = "unnamed_compute_pipeline")
        {
            Name = name;
            BindGroups = bindGroups;
            Source = computeShader;
            PushConstantsRanges = pushConstantsRanges;
        }

        public ShaderModule Source { get; init; }
        public GPUBindGroup[] BindGroups { get; init; }
        public PushConstantsRange[]? PushConstantsRanges { get; init; }
        public string Name { get; init; } = "unnamed_compute_pipeline";


    }
}