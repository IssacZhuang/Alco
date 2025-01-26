namespace Alco.Graphics
{
    public struct ComputePipelineDescriptor
    {
        public ComputePipelineDescriptor(
            ShaderModule computeShader,
            GPUBindGroup[] bindGroups,
            string name = "unnamed_compute_pipeline")
        {
            Name = name;
            BindGroups = bindGroups;
            Source = computeShader;
        }

        public ShaderModule Source { get; init; }
        public GPUBindGroup[] BindGroups { get; init; }
        public string Name { get; init; } = "unnamed_compute_pipeline";


    }
}