using System.Collections.Concurrent;
using StbImageSharp;
using Alco.Graphics;

using static Alco.UtilsMemory;

namespace Alco.Rendering;

// texture factory

public partial class RenderingSystem
{
    /// <summary>
    /// Creates a Texture2D from a stream.
    /// </summary>
    /// <param name="stream">The stream containing image data.</param>
    /// <param name="option">Image load options.</param>
    /// <returns>A new Texture2D instance.</returns>
    public unsafe Texture2D CreateTexture2DFromStream(
        Stream stream,
        ImageLoadOption? option = null
    )
    {
        ColorComponents targetComponents = ColorComponents.RedGreenBlueAlpha;
        using ImageResultBuffer image = ImageResultBuffer.FromStream(stream, targetComponents);

        return CreateTexture2D(
            image.UnsafePointer,
            (uint)image.Data.Length,
            (uint)image.Width,
            (uint)image.Height,
            option
        );
    }

    /// <summary>
    /// Creates a Texture2D from file bytes.
    /// </summary>
    /// <param name="fileBytes">The file bytes containing image data.</param>
    /// <param name="option">Image load options.</param>
    /// <returns>A new Texture2D instance.</returns>
    public unsafe Texture2D CreateTexture2DFromFile(
        ReadOnlySpan<byte> fileBytes,
        ImageLoadOption? option = null
    )
    {
        ColorComponents targetComponents = ColorComponents.RedGreenBlueAlpha;
        using ImageResultBuffer image = ImageResultBuffer.FromMemory(fileBytes, targetComponents);

        return CreateTexture2D(
            image.UnsafePointer,
            (uint)image.Data.Length,
            (uint)image.Width,
            (uint)image.Height,
            option
        );
    }

    /// <summary>
    /// Creates a Texture2D with a solid color.
    /// </summary>
    /// <param name="width">The width of the texture.</param>
    /// <param name="height">The height of the texture.</param>
    /// <param name="color">The solid color to fill the texture with.</param>
    /// <param name="option">Image load options.</param>
    /// <returns>A new Texture2D instance.</returns>
    public unsafe Texture2D CreateTexture2D(
        uint width,
        uint height,
        Color32 color,
        ImageLoadOption? option = null
    )
    {
        int length = (int)(width * height);
        Color32* data = Alloc<Color32>(length);
        Memset(data, length, color);
        Texture2D texture = CreateTexture2D(
            (byte*)data,
            (uint)sizeof(Color32) * width * height,
            width,
            height,
            option
        );
        Free(data);
        return texture;
    }

    /// <summary>
    /// Creates a Texture2D from raw data.
    /// </summary>
    /// <param name="data">The raw image data.</param>
    /// <param name="width">The width of the texture.</param>
    /// <param name="height">The height of the texture.</param>
    /// <param name="option">Image load options.</param>
    /// <returns>A new Texture2D instance.</returns>
    public unsafe Texture2D CreateTexture2D(
        ReadOnlySpan<byte> data,
        uint width,
        uint height,
        ImageLoadOption? option = null
    )
    {
        fixed (byte* ptr = data)
        {
            return CreateTexture2D(
                ptr,
                (uint)data.Length,
                width,
                height,
                option
            );
        }
    }

    /// <summary>
    /// Creates a Texture2D from raw data pointer.
    /// </summary>
    /// <param name="data">Pointer to the raw image data.</param>
    /// <param name="size">Size of the data in bytes.</param>
    /// <param name="width">The width of the texture.</param>
    /// <param name="height">The height of the texture.</param>
    /// <param name="option">Image load options.</param>
    /// <returns>A new Texture2D instance.</returns>
    public unsafe Texture2D CreateTexture2D(
        byte* data,
        uint size,
        uint width,
        uint height,
        ImageLoadOption? option = null
    )
    {
        ImageLoadOption optionReal = option ?? ImageLoadOption.Default;
        GPUSampler sampler = _device.GetSampler(optionReal.FilterMode, optionReal.AddressMode);

        CreateTextureCore(width, height, option, out GPUTexture texture, out GPUTextureView textureView);

        _device.WriteTexture(
            texture,
            data,
            size
        );

        return new Texture2D(
            _device,
            texture,
            textureView,
            sampler
        );
    }

    /// <summary>
    /// Creates a Texture2D from raw data pointer with a custom sampler.
    /// </summary>
    /// <param name="data">Pointer to the raw image data.</param>
    /// <param name="size">Size of the data in bytes.</param>
    /// <param name="width">The width of the texture.</param>
    /// <param name="height">The height of the texture.</param>
    /// <param name="sampler">Custom GPU sampler to use.</param>
    /// <param name="option">Image load options.</param>
    /// <returns>A new Texture2D instance.</returns>
    public unsafe Texture2D CreateTexture2D(
        byte* data,
        uint size,
        uint width,
        uint height,
        GPUSampler sampler,
        ImageLoadOption? option = null
    )
    {
        CreateTextureCore(width, height, option, out GPUTexture texture, out GPUTextureView textureView);

        _device.WriteTexture(
            texture,
            data,
            size
        );

        return new Texture2D(
            _device,
            texture,
            textureView,
            sampler
        );
    }

    /// <summary>
    /// Creates an empty Texture2D.
    /// </summary>
    /// <param name="width">The width of the texture.</param>
    /// <param name="height">The height of the texture.</param>
    /// <param name="option">Image load options.</param>
    /// <returns>A new Texture2D instance.</returns>
    public unsafe Texture2D CreateTexture2D(
        uint width,
        uint height,
        ImageLoadOption? option = null
    )
    {
        ImageLoadOption optionReal = option ?? ImageLoadOption.Default;
        GPUSampler sampler = _device.GetSampler(optionReal.FilterMode, optionReal.AddressMode);

        CreateTextureCore(width, height, option, out GPUTexture texture, out GPUTextureView textureView);

        return new Texture2D(
            _device,
            texture,
            textureView,
            sampler
        );
    }

    /// <summary>
    /// Creates an empty Texture2D with a custom sampler.
    /// </summary>
    /// <param name="width">The width of the texture.</param>
    /// <param name="height">The height of the texture.</param>
    /// <param name="sampler">Custom GPU sampler to use.</param>
    /// <param name="option">Image load options.</param>
    /// <returns>A new Texture2D instance.</returns>
    public unsafe Texture2D CreateTexture2D(
        uint width,
        uint height,
        GPUSampler sampler,
        ImageLoadOption? option = null
    )
    {
        CreateTextureCore(width, height, option, out GPUTexture texture, out GPUTextureView textureView);

        return new Texture2D(
            _device,
            texture,
            textureView,
            sampler
        );
    }

    /// <summary>
    /// Creates a Texture2D from existing GPU resources.
    /// </summary>
    /// <param name="texture">The GPU texture.</param>
    /// <param name="textureView">The GPU texture view.</param>
    /// <param name="sampler">The GPU sampler.</param>
    /// <returns>A new Texture2D instance.</returns>
    public Texture2D CreateTexture2D(
        GPUTexture texture,
        GPUTextureView textureView,
        GPUSampler sampler  
    )
    {
        return new Texture2D(
            _device,
            texture,
            textureView,
            sampler
        );
    }

    /// <summary>
    /// Writes image file data to an existing GPU texture.
    /// </summary>
    /// <param name="file">The file bytes containing image data.</param>
    /// <param name="texture">The target GPU texture.</param>
    public unsafe void WriteImageFileToTexture(ReadOnlySpan<byte> file, GPUTexture texture)
    {
        using ImageResultBuffer image = ImageResultBuffer.FromMemory(file, ColorComponents.RedGreenBlueAlpha);
        _device.WriteTexture(texture, image.UnsafePointer, (uint)image.Data.Length);
    }

    /// <summary>
    /// Creates the core GPU texture and texture view.
    /// </summary>
    /// <param name="width">The width of the texture.</param>
    /// <param name="height">The height of the texture.</param>
    /// <param name="option">Image load options.</param>
    /// <param name="texture">The created GPU texture.</param>
    /// <param name="textureView">The created GPU texture view.</param>
    public void CreateTextureCore(uint width, uint height, ImageLoadOption? option, out GPUTexture texture, out GPUTextureView textureView)
    {
        ImageLoadOption optionReal = option ?? ImageLoadOption.Default;
        TextureDescriptor textureDescriptor = new TextureDescriptor(
            TextureDimension.Texture2D,
            optionReal.Format,
            width,
            height,
            1,
            optionReal.MipLevels,
            optionReal.Usage,
            1,
            optionReal.Name
        );

        texture = _device.CreateTexture(textureDescriptor);

        TextureViewDescriptor textureViewDescriptor = new TextureViewDescriptor(
            texture,
            TextureViewDimension.Texture2D
        );

        textureView = _device.CreateTextureView(textureViewDescriptor);
    }

    /// <summary>
    /// Creates a BC3 texture compressor.
    /// </summary>
    /// <param name="material">The compute material to use for compression.</param>
    /// <returns>A new TextureCompressorBC3 instance.</returns>
    public TextureCompressorBC3 CreateTextureCompressorBC3(ComputeMaterial material)
    {
        return new TextureCompressorBC3(this, material);
    }
}