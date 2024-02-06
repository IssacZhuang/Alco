namespace Vocore.Graphics;

public enum BindingType
{
    Undefined = 0,
    UniformBuffer = 1,
    StorageBuffer = 2,
    Sampler = 3,
    TextureView = 4,
    StorageTexture = 5,  // also known as RWTexture in HLSL or RenderTexture in Unity
}