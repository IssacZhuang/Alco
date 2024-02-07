namespace Vocore.Graphics;

public struct ImageLoadOption
{
    public static readonly ImageLoadOption Default = new ImageLoadOption();

    public ImageLoadOption(
        bool isSRGB = false,
        uint mipLevels = 1,
        string name = "unnamed_texture"
    )
    {
        IsSRGB = isSRGB;
        MipLevels = mipLevels;
        Name = name;
    }

    public bool IsSRGB { get; init; } = false;
    public uint MipLevels { get; init; } = 1;
    public string Name { get; init; } = "unnamed_texture";
}