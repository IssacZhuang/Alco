using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// Sprite and texture atlas creation.
/// </summary>
public partial class RenderingSystem
{
    /// <summary>
    /// Creates a texture atlas packer.
    /// </summary>
    /// <param name="blitMaterial">The material used to blit textures into the atlas.</param>
    /// <param name="width">The initial atlas width in pixels.</param>
    /// <param name="height">The initial atlas height in pixels.</param>
    /// <param name="format">The atlas pixel format.</param>
    /// <returns>The new texture atlas packer.</returns>
    public TextureAtlasPacker CreateTextureAtlasPacker(GraphicsMaterial blitMaterial, int width = 256, int height = 256, PixelFormat format = PixelFormat.RGBA8Unorm)
    {
        return new TextureAtlasPacker(this, format, blitMaterial, width, height);
    }
}