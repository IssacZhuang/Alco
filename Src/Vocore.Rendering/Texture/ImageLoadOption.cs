using Vocore.Graphics;

namespace Vocore.Rendering;

public struct ImageLoadOption
{
    public static readonly ImageLoadOption Default = new ImageLoadOption
    {
        Format = PixelFormat.RGBA8Unorm,
        Usage = TextureUsage.Standard,
        MipLevels = 1,
        Name = "unnamed_texture"
    };

    public ImageLoadOption(
        PixelFormat format = PixelFormat.RGBA8Unorm,
        TextureUsage usage = TextureUsage.Standard,
        uint mipLevels = 1,
        string name = "unnamed_texture"
    )
    {
        Format = format;
        MipLevels = mipLevels;
        Usage = usage;
        Name = name;
    }

    public PixelFormat Format { get; init; } = PixelFormat.RGBA8Unorm;
    public uint MipLevels { get; init; } = 1;
    public TextureUsage Usage { get; init; } = TextureUsage.Standard;
    public string Name { get; init; } = "unnamed_texture";
}