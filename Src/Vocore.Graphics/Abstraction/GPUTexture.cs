namespace Vocore.Graphics;

/// <summary>
/// The texture in the VRAM
/// </summary>
public abstract class GPUTexture : BaseGPUObject
{
    public abstract uint Width { get; }
    public abstract uint Height { get; }
    public abstract uint Depth { get; }
    public abstract uint MipLevelCount { get; }
    public abstract PixelFormat PixelFormat { get; }

    public uint GetMipWidth(uint mipLevel)
    {
        return Math.Max(1, Width >> (int)mipLevel);
    }

    public uint GetMipHeight(uint mipLevel)
    {
        return Math.Max(1, Height >> (int)mipLevel);
    }

    public uint GetMipDepth(uint mipLevel)
    {
        return Math.Max(1, Depth >> (int)mipLevel);
    }

    protected GPUTexture(string name): base(name)
    {
    }

    protected GPUTexture(in TextureDescriptor descriptor): base(descriptor.Name)
    {
        if(descriptor.Format == PixelFormat.Undefined)
            throw new ArgumentException("Format cannot be undefined", nameof(descriptor));
        if (descriptor.Width <= 0)
            throw new ArgumentException("Width cannot be less than or equal to 0", nameof(descriptor));
        if (descriptor.Height <= 0) 
            throw new ArgumentException("Height cannot be less than or equal to     0", nameof(descriptor));
        if (descriptor.DepthOrArrayLayer <= 0)
            throw new ArgumentException("Depth cannot be less than or equal to 0", nameof(descriptor));
        if (descriptor.MipLevels <= 0)
            throw new ArgumentException("MipLevels cannot be less than or equal to 0", nameof(descriptor));
    }
}