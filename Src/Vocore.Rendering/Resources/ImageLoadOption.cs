using Vocore.Graphics;

namespace Vocore.Rendering;

public struct ImageLoadOption
{
    public static readonly ImageLoadOption Default = new ImageLoadOption
    {
        IsSRGB = false,
        MipLevels = 1,
        Usage = TextureUsage.Standard,
        Name = "unnamed_texture"
    };

    public ImageLoadOption(
        bool isSRGB = false,
        uint mipLevels = 1,
        TextureUsage usage = TextureUsage.Standard,
        string name = "unnamed_texture"
    )
    {
        IsSRGB = isSRGB;
        MipLevels = mipLevels;
        Usage = usage;
        Name = name;
    }

    public bool IsSRGB { get; init; } = false;
    public uint MipLevels { get; init; } = 1;
    public TextureUsage Usage { get; init; } = TextureUsage.Standard;
    public string Name { get; init; } = "unnamed_texture";
}