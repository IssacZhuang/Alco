using System.Numerics;
using StbImageSharp;

using static Vocore.Graphics.UtilsInterop;

namespace Vocore.Graphics;

public static class DeviceResourceExtension
{
    public unsafe static Texture2D CreateTexture2DEmpty(
        this GPUDevice device,
        uint width,
        uint height,
        ColorFloat color,
        ImageLoadOption? option = null
    )
    {
        int length = (int)(width * height);
        Color32* data = Alloc<Color32>(length);
        Memset(data, length, color.ToColor32());
        Texture2D texture = CreateTexture2DFromData(
            device,
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

    public static Texture2D CreateTexture2DFromFile(
        this GPUDevice device,
        Stream stream,
        ImageLoadOption? option = null
    )
    {

        ColorComponents targetComponents = ColorComponents.RedGreenBlueAlpha;
        ImageResult image = ImageResult.FromStream(stream, targetComponents);

        return CreateTexture2DFromData(
            device,
            image.Data,
            (uint)image.Width,
            (uint)image.Height,
            GetPixelSize(targetComponents),
            option
        );
    }

    public static Texture2D CreateTexture2DFromFile(
        this GPUDevice device,
        byte[] data,
        ImageLoadOption? option = null
    )
    {
        ColorComponents targetComponents = ColorComponents.RedGreenBlueAlpha;
        ImageResult image = ImageResult.FromMemory(data, targetComponents);

        return CreateTexture2DFromData(
            device,
            image.Data,
            (uint)image.Width,
            (uint)image.Height,
            GetPixelSize(targetComponents),
            option
        );
    }

    public unsafe static Texture2D CreateTexture2DFromData(
        this GPUDevice device,
        byte[] data,
        uint width,
        uint height,
        uint pixelSize = 4,
        ImageLoadOption? option = null
    )
    {
        fixed (byte* ptr = data)
        {
            return CreateTexture2DFromData(
                device,
                ptr,
                (uint)data.Length,
                width,
                height,
                pixelSize,
                option
            );
        }
    }

    public unsafe static Texture2D CreateTexture2DFromData(
        this GPUDevice device,
        byte* data,
        uint size,
        uint width,
        uint height,
        uint pixelSize = 4,
        ImageLoadOption? option = null
    )
    {
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



    private static uint GetPixelSize(ColorComponents components)
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