namespace Vocore.Graphics;

/// <summary>
/// A GPUTexture with a TextureView which the dimension is 2D
/// </summary>
public class Texture2D : BaseGPUObject
{
    private readonly GPUTexture _texture;
    private readonly GPUTextureView _textureView;

    public override string Name => throw new NotImplementedException();

    public Texture2D(GPUTexture texture, GPUTextureView textureView)
    {
        _texture = texture;
        _textureView = textureView;
    }

    protected override void Dispose(bool disposing)
    {
        _texture.Dispose();
        _textureView.Dispose();
    }

    
}