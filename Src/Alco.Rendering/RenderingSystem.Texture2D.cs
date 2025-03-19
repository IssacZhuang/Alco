using System.Collections.Concurrent;
using StbImageSharp;
using Alco.Graphics;

using static Alco.Unsafe.UtilsMemory;

namespace Alco.Rendering;

public partial class RenderingSystem
{
    public unsafe Texture2D CreateTexture2DFromStream(
        Stream stream,
        ImageLoadOption? option = null
    )
    {

        ColorComponents targetComponents = ColorComponents.RedGreenBlueAlpha;
        using ImageResultBuffer image = ImageResultBuffer.FromStream(stream, targetComponents);

        return CreateTexture2D(
            image.Memory.Pointer,
            (uint)image.Memory.Length,
            (uint)image.Width,
            (uint)image.Height,
            option
        );
    }

    public unsafe Texture2D CreateTexture2DFromFile(
        byte[] fileBytes,
        ImageLoadOption? option = null
    )
    {
        ColorComponents targetComponents = ColorComponents.RedGreenBlueAlpha;
        using ImageResultBuffer image = ImageResultBuffer.FromMemory(fileBytes, targetComponents);

        return CreateTexture2D(
            image.Memory.Pointer,
            (uint)image.Memory.Length,
            (uint)image.Width,
            (uint)image.Height,
            option
        );
    }

    public unsafe Texture2D CreateTexture2DFromFile(
        ReadOnlySpan<byte> fileBytes,
        ImageLoadOption? option = null
    )
    {
        ColorComponents targetComponents = ColorComponents.RedGreenBlueAlpha;
        using ImageResultBuffer image = ImageResultBuffer.FromMemory(fileBytes, targetComponents);

        return CreateTexture2D(
            image.Memory.Pointer,
            (uint)image.Memory.Length,
            (uint)image.Width,
            (uint)image.Height,
            option
        );
        
    }

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

    public unsafe Texture2D CreateTexture2D(
        byte* data,
        uint size,
        uint width,
        uint height,
        ImageLoadOption? option = null
    )
    {
        return CreateTexture2D(data, size, width, height, _device.SamplerLinearRepeat, option);
    }

    public unsafe Texture2D CreateTexture2D(
        byte* data,
        uint size,
        uint width,
        uint height,
        FilterMode filterMode,
        ImageLoadOption? option = null
    )
    {
        GPUSampler sampler = _device.GetSampler(filterMode, AddressMode.ClampToEdge);
        return CreateTexture2D(data, size, width, height, sampler, option);
    }

    public unsafe Texture2D CreateTexture2D(
        byte* data,
        uint size,
        uint width,
        uint height,
        FilterMode filterMode,
        AddressMode addressMode,
        ImageLoadOption? option = null
    )
    {
        GPUSampler sampler = _device.GetSampler(filterMode, addressMode);
        return CreateTexture2D(data, size, width, height, sampler, option);
    }

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

    public unsafe void WriteImageFileToTexture(ReadOnlySpan<byte> file, GPUTexture texture)
    {
        using ImageResultBuffer image = ImageResultBuffer.FromMemory(file, ColorComponents.RedGreenBlueAlpha);
        _device.WriteTexture(texture, image.Memory.Pointer, (uint)image.Memory.Length);
    }

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


    public TextureCompressorBC3 CreateTextureCompressorBC3(ComputeMaterial material)
    {
        return new TextureCompressorBC3(this, material);
    }

}