namespace Vocore.Graphics
{
    public struct ComputePipelineDescriptor
    {
        public ComputePipelineDescriptor(
            ShaderStageSource computeShader,
            BindGroupDescriptor[] resourceLayouts,
            string name = "Unnamed Compute Pipeline")
        {
            Name = name;
            ResourceLayouts = resourceLayouts;
            Source = computeShader;
        }

        public ShaderStageSource Source { get; init; }
        public BindGroupDescriptor[] ResourceLayouts { get; init; }
        public string Name { get; init; } = "Unnamed Compute Pipeline";


    }
}