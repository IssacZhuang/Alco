namespace Vocore.Graphics
{
    public struct ComputePipelineDescriptor
    {
        public string Name { get; init; } = "Unnamed Compute Pipeline";
        public ShaderStageSource Source { get; init; }

        public ComputePipelineDescriptor(ShaderStageSource computeShader, string name = "Unnamed Compute Pipeline")
        {
            Name = name;
            Source = computeShader;
        }
    }
}