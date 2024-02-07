using StbImageSharp;

namespace Vocore.Graphics;

public static class DeviceResourceExtension
{
    public static Texture2D CreateTexture2DFromFile(
        this GPUDevice device,
        Stream stream,
        ImageLoadOption option
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
        byte* data,
        uint size,
        uint width,
        uint height,
        uint pixelSize = 4,
        ImageLoadOption? option = null
    )
    {
        TextureDescriptor textureDescriptor = new TextureDescriptor(
            TextureDimension.Texture2D,
            PixelFormat.RGBA8Unorm,
            width,
            height,
            1,
            1,
            TextureUsage.TextureBinding | TextureUsage.Write
        );

        ImageLoadOption optionReal = option ?? ImageLoadOption.Default;

        if (optionReal.IsSRGB) textureDescriptor.Format = PixelFormat.RGBA8UnormSrgb;

        textureDescriptor.Name = optionReal.Name;

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