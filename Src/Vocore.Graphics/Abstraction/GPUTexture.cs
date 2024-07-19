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
}