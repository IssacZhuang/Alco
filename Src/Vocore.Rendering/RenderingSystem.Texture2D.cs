using StbImageSharp;
using Vocore.Graphics;

using static Vocore.Unsafe.UtilsMemory;

namespace Vocore.Rendering;

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
        byte[] data,
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
        GPUDevice device = _device;
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

        GPUTexture texture = device.CreateTexture(textureDescriptor);

        device.WriteTexture(
            texture,
            data,
            size
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

    public unsafe Texture2D CreateTexture2D(
        uint width,
        uint height,
        ImageLoadOption? option = null
    )
    {
        GPUDevice device = _device;
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

        GPUTexture texture = device.CreateTexture(textureDescriptor);

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

}