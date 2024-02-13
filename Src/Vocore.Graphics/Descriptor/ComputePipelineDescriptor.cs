namespace Vocore.Graphics
{
    public struct ComputePipelineDescriptor
    {
        public ComputePipelineDescriptor(
            ShaderStageSource computeShader,
            GPUBindGroup[] bindGroups,
            string name = "unnamed_compute_pipeline")
        {
            Name = name;
            BindGroups = bindGroups;
            Source = computeShader;
        }

        public ShaderStageSource Source { get; init; }
        public GPUBindGroup[] BindGroups { get; init; }
        public string Name { get; init; } = "unnamed_compute_pipeline";


    }
}