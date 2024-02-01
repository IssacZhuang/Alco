namespace Vocore.Graphics
{
    public struct ComputePipelineDescriptor
    {
        public ComputePipelineDescriptor(
            ShaderStageSource computeShader,
            ResourceBindingLayout[] resourceLayouts,
            string name = "Unnamed Compute Pipeline")
        {
            Name = name;
            ResourceLayouts = resourceLayouts;
            Source = computeShader;
        }

        public ShaderStageSource Source { get; init; }
        public ResourceBindingLayout[] ResourceLayouts { get; init; }
        public string Name { get; init; } = "Unnamed Compute Pipeline";


    }
}