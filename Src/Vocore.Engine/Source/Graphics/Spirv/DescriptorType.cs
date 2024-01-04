

namespace Vocore.Engine
{
    /// <summary>
    /// Represents the type of a descriptor in a graphics pipeline.
    /// </summary>
    public enum DescriptorType
    {
        Sampler = 0,
        CombinedImageSampler = 1,
        SampledImage = 2,
        StorageImage = 3,
        UniformTexelBuffer = 4,
        StorageTexelBuffer = 5,
        UniformBuffer = 6,
        StorageBuffer = 7,
        UniformBufferDynamic = 8,
        StorageBufferDynamic = 9,
        InputAttachment = 10,
        AccelerationStructureKhr = 1000150000
    }


}