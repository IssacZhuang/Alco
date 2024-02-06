using StbImageSharp;

namespace Vocore.Graphics;

public static class DeviceResourceExtension
{
    public unsafe static Texture2D CreateTexture2D(
        this GPUDevice device,
        Stream stream,
        bool generateMipmaps = false,
        string name = "unnamed_texture"
    )
    {
        ImageResult image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
        TextureDescriptor textureDescriptor = new TextureDescriptor(
            TextureDimension.Texture2D,
            PixelFormat.RGBA8UnormSrgb,
            (uint)image.Width,
            (uint)image.Height,
            1,
            1,
            TextureUsage.TextureBinding | TextureUsage.Write
        );

        textureDescriptor.Name = name;

        GPUTexture texture = device.CreateTexture(textureDescriptor);

        uint pixelSize = 4;

        fixed (byte* ptr = image.Data)
        {
            device.WriteTexture(
                texture,
                ptr,
                (uint)image.Width * pixelSize * (uint)image.Height * pixelSize,
                pixelSize
            );
        }


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
}