using Vocore.Graphics;

namespace Vocore.Rendering;

public partial class RenderingSystem
{
    public TextureAtlasPacker CreateTextureAtlasPacker(Material blitMaterial, int width = 256, int height = 256, PixelFormat format = PixelFormat.RGBA8Unorm)
    {
        return new TextureAtlasPacker(this, format, blitMaterial, width, height);
    }
}