using Alco.Graphics;

namespace Alco.Rendering;

// 3D texture factory

public partial class RenderingSystem
{
    /// <summary>
    /// Creates an empty Texture3D with a mip chain. The default usage
    /// (<see cref="TextureUsage.TextureBinding"/> | <see cref="TextureUsage.StorageBinding"/>)
    /// allows both filtered sampling and per-mip storage writes from compute passes.
    /// </summary>
    /// <param name="width">The width of the texture.</param>
    /// <param name="height">The height of the texture.</param>
    /// <param name="depth">The depth of the texture.</param>
    /// <param name="format">The pixel format of the texture.</param>
    /// <param name="mipLevels">The number of mip levels.</param>
    /// <param name="usage">The texture usage flags.</param>
    /// <param name="sampler">The sampler; null uses the shared linear clamp-to-edge sampler.</param>
    /// <param name="name">The texture name for debugging.</param>
    /// <returns>A new Texture3D instance.</returns>
    public Texture3D CreateTexture3D(
        uint width,
        uint height,
        uint depth,
        PixelFormat format = PixelFormat.RGBA16Float,
        uint mipLevels = 1,
        TextureUsage usage = TextureUsage.TextureBinding | TextureUsage.StorageBinding,
        GPUSampler? sampler = null,
        string name = "texture_3d"
    )
    {
        TextureDescriptor textureDescriptor = new TextureDescriptor(
            TextureDimension.Texture3D,
            format,
            width,
            height,
            depth,
            mipLevels,
            usage,
            1,
            name
        );

        GPUTexture texture = _device.CreateTexture(textureDescriptor);

        TextureViewDescriptor textureViewDescriptor = new TextureViewDescriptor(
            texture,
            TextureViewDimension.Texture3D,
            0,
            mipLevels
        );

        GPUTextureView textureView = _device.CreateTextureView(textureViewDescriptor);
        GPUSampler samplerReal = sampler ?? _device.GetSampler(FilterMode.Linear, AddressMode.ClampToEdge);

        return new Texture3D(
            _device,
            texture,
            textureView,
            samplerReal
        );
    }
}
