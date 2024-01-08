using Graphics;

namespace Vocore.Graphics;

public struct GPUTextureDescriptor
{
    public GPUTextureDescriptor(

        TextureDimension dimension,
        PixelFormat format,
        uint width,
        uint height,
        uint depthOrArrayLayer = 1,
        uint mipLevels = 1,
        TextureUsage usage = TextureUsage.Read,
        uint sampleCount = 1,
        AccessMode accessMode = AccessMode.None,
        string name = "Unnamed GPU texture"
        )
    {
        UtilsAssert.IsTrue(format != PixelFormat.Undefined, "Texture format must be defined");
        UtilsAssert.IsTrue(width > 0, "Texture width must be greater than 0");
        UtilsAssert.IsTrue(height > 0, "Texture height must be greater than 0");
        UtilsAssert.IsTrue(depthOrArrayLayer > 0, "Texture depth or array layer must be greater than 0");
        UtilsAssert.IsTrue(mipLevels > 0, "Texture mip levels must be greater than 0");
        
        Name = name;
        Format = format;
        Width = width;
        Height = height;
        DepthOrArrayLayer = depthOrArrayLayer;
        MipLevels = mipLevels;
        SampleCount = sampleCount;
        AccessMode = accessMode;
        Dimension = dimension;
        Usage = usage;
    }
    public TextureDimension Dimension { get; set; } = TextureDimension.Texture2D;
    public PixelFormat Format;
    public AccessMode AccessMode { get; set; } = AccessMode.None;
    public TextureUsage Usage { get; set; } = TextureUsage.Read;
    public uint Width { get; set; } = 1;
    public uint Height { get; set; } = 1;
    public uint DepthOrArrayLayer { get; set; } = 1;
    public uint MipLevels { get; set; } = 1;
    public uint SampleCount { get; set; } = 1;
    public string? Name { get; set; } = "Unnamed GPU texture";
}