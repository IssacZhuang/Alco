using System.Runtime.CompilerServices;

namespace Vocore.Graphics;

/// <summary>
/// A GPUTexture with a TextureView which the dimension is 2D
/// </summary>
public class Texture2D : BaseGPUObject, IGPUResources
{
    private readonly GPUResourceGroup _resources;

    // internal
    private readonly GPUTexture _texture;
    private readonly GPUTextureView _textureView;

    // from outside
    private readonly GPUSampler _sampler;

    public override string Name { get; }

    public GPUResourceGroup Resources
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _resources;
    }

    internal Texture2D(
        GPUDevice device,
        GPUTexture texture,
        GPUTextureView textureView,
        GPUSampler sampler)
    {
        _texture = texture;
        _textureView = textureView;
        _sampler = sampler;

        Name = texture.Name;

        ResourceGroupDescriptor descriptor = new ResourceGroupDescriptor(
            device.BindGroupTexture2D,
            new ResourceBindingEntry[]{
                new ResourceBindingEntry(0, _textureView),
                new ResourceBindingEntry(1, _sampler)
            }
        );

        _resources = device.CreateResourceGroup(descriptor);
    }

    protected override void Dispose(bool disposing)
    {
        _texture.Dispose();
        _textureView.Dispose();
        //the sampler is not disposed here because it is a shared resource
    }


}