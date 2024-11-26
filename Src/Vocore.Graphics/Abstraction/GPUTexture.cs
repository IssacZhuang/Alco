namespace Vocore.Graphics;

/// <summary>
/// The texture in the VRAM
/// </summary>
public abstract class GPUTexture : BaseGPUObject
{
    //it might be a dynamic texture so the width, height and pixel format might be changed

    /// <summary>
    /// The width of the texture    
    /// </summary>
    /// <value>The width of the texture</value>
    public abstract uint Width { get; }
    /// <summary>
    /// The height of the texture
    /// </summary>
    /// <value>The height of the texture</value>
    public abstract uint Height { get; }
    /// <summary>
    /// The depth of the texture
    /// </summary>
    /// <value>The depth of the texture</value>
    public abstract uint Depth { get; }
    /// <summary>
    /// The mipmap count(including the base level) in the texture
    /// </summary>
    /// <value>The mipmap count in the texture</value>
    public abstract uint MipLevelCount { get; }
    /// <summary>
    /// The pixel format of the texture
    /// </summary>
    /// <value>The pixel format of the texture</value>
    public abstract PixelFormat PixelFormat { get; }

    /// <summary>
    /// Get the width of the texture at the specified mip level. level zero is the base level
    /// </summary>
    /// <param name="mipLevel">The mip level</param>
    /// <returns>The width of the texture at the specified mip level</returns>
    public uint GetMipWidth(uint mipLevel)
    {
        return Math.Max(1, Width >> (int)mipLevel);
    }

    /// <summary>
    /// Get the height of the texture at the specified mip level. level zero is the base level
    /// </summary>
    /// <param name="mipLevel">The mip level</param>
    /// <returns>The height of the texture at the specified mip level</returns>
    public uint GetMipHeight(uint mipLevel)
    {
        return Math.Max(1, Height >> (int)mipLevel);
    }

    /// <summary>
    /// Get the depth of the texture at the specified mip level. level zero is the base level
    /// </summary>
    /// <param name="mipLevel">The mip level</param>
    /// <returns>The depth of the texture at the specified mip level</returns>
    public uint GetMipDepth(uint mipLevel)
    {
        return Math.Max(1, Depth >> (int)mipLevel);
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