namespace Vocore.Graphics;

public enum BindingType
{
    Undefined = 0,
    // use GPUBuffer
    UniformBuffer = 1,
    StorageBuffer = 2,
    // use GPUSampler
    Sampler = 3,
    // use GPUTextureView
    Texture = 4,
    StorageTexture = 5,  // also known as RWTexture in HLSL or RenderTexture in Unity
}