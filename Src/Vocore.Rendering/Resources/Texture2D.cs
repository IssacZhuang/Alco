using Vocore.Graphics;
using StbImageSharp;
using static Vocore.Unsafe.UtilsMemory;
using System.Runtime.CompilerServices;

namespace Vocore.Rendering;

public class Texture2D : Texture
{
    public override bool IsReadOnly
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => true;
    }

    internal Texture2D(GPUDevice device, GPUTexture texture, GPUTextureView textureView, GPUSampler sampler) : base(device, texture, textureView, sampler)
    {

    }

    public unsafe static Texture2D CreateEmpty(
        uint width,
        uint height,
        Color32 color,
        ImageLoadOption? option = null
    )
    {
        int length = (int)(width * height);
        Color32* data = Alloc<Color32>(length);
        Memset(data, length, color);
        Texture2D texture = CreateFromData(
            (byte*)data,
            (uint)sizeof(Color32) * width * height,
            width,
            height,
            4,
            option
        );
        Free(data);
        return texture;
    }

    public unsafe static Texture2D CreateFromStream(
        Stream stream,
        ImageLoadOption? option = null
    )
    {

        ColorComponents targetComponents = ColorComponents.RedGreenBlueAlpha;
        using ImageResultBuffer image = ImageResultBuffer.FromStream(stream, targetComponents);

        return CreateFromData(
            image.Memory.Pointer,
            image.Memory.Length,
            (uint)image.Width,
            (uint)image.Height,
            GetPixelSize(targetComponents),
            option
        );
    }

    public unsafe static Texture2D CreateFromFile(
        byte[] fileBytes,
        ImageLoadOption? option = null
    )
    {
        ColorComponents targetComponents = ColorComponents.RedGreenBlueAlpha;
        using ImageResultBuffer image = ImageResultBuffer.FromMemory(fileBytes, targetComponents);

        return CreateFromData(
            image.Memory.Pointer,
            image.Memory.Length,
            (uint)image.Width,
            (uint)image.Height,
            GetPixelSize(targetComponents),
            option
        );
    }

    public unsafe static Texture2D CreateFromData(
        byte[] data,
        uint width,
        uint height,
        uint pixelSize = 4,
        ImageLoadOption? option = null
    )
    {
        fixed (byte* ptr = data)
        {
            return CreateFromData(
                ptr,
                (uint)data.Length,
                width,
                height,
                pixelSize,
                option
            );
        }
    }

    public unsafe static Texture2D CreateFromData(
        byte* data,
        uint size,
        uint width,
        uint height,
        uint pixelSize = 4,
        ImageLoadOption? option = null
    )
    {
        GPUDevice device = GetDevice();
        ImageLoadOption optionReal = option ?? ImageLoadOption.Default;
        TextureDescriptor textureDescriptor = new TextureDescriptor(
            TextureDimension.Texture2D,
            optionReal.IsSRGB ? PixelFormat.RGBA8UnormSrgb : PixelFormat.RGBA8Unorm,
            width,
            height,
            1,
            optionReal.MipLevels,
            optionReal.Usage,
            1,
            optionReal.Name
        );

        GPUTexture texture = device.CreateTexture(textureDescriptor);

        device.WriteTexture(
            texture,
            data,
            size,
            pixelSize
        );

        TextureViewDescriptor textureViewDescriptor = new TextureViewDescriptor(
            texture,
            TextureViewDimension.Texture2D
        );

        textureDescriptor.Name = optionReal.Name;

        GPUTextureView textureView = device.CreateTextureView(textureViewDescriptor);

        return new Texture2D(
            device,
            texture,
            textureView,
            device.SamplerLinearRepeat
        );
    }

    public unsafe static Texture2D CreateByFormat(
        uint width,
        uint height,
        PixelFormat format,
        uint mipLevel,
        TextureUsage usage = TextureUsage.Standard,
        string name = "unnamed_texture"
    )
    {
        GPUDevice device = GetDevice();
        TextureDescriptor textureDescriptor = new TextureDescriptor(
            TextureDimension.Texture2D,
            format,
            width,
            height,
            1,
            mipLevel,
            usage,
            1,
            name
        );

        GPUTexture texture = device.CreateTexture(textureDescriptor);

        TextureViewDescriptor textureViewDescriptor = new TextureViewDescriptor(
            texture,
            TextureViewDimension.Texture2D
        );

        textureDescriptor.Name = name;

        GPUTextureView textureView = device.CreateTextureView(textureViewDescriptor);

        return new Texture2D(
            device,
            texture,
            textureView,
            device.SamplerLinearRepeat
        );
    }


    public static uint GetPixelSize(ColorComponents components)
    {
        switch (components)
        {
            case ColorComponents.RedGreenBlueAlpha:
                return 4;
            case ColorComponents.RedGreenBlue:
                return 3;
            case ColorComponents.GreyAlpha:
                return 2;
            case ColorComponents.Grey:
                return 1;
            default:
                throw new NotSupportedException("The color components is not supported");
        }
    }
}