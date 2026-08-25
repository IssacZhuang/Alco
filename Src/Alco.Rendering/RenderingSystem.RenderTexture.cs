using Alco.Graphics;

namespace Alco.Rendering;

// render texture factory

public partial class RenderingSystem
{
    /// <summary>
    /// Create a render texture with the given attachment layout, width, height and name.
    /// </summary>
    /// <param name="attachmentLayout"> The attachment layout to create the render texture. </param>
    /// <param name="width"> The width of the render texture. </param>
    /// <param name="height"> The height of the render texture. </param>
    /// <param name="name"> The name of the render texture. </param>
    /// <returns></returns>
    public RenderTexture CreateRenderTexture(
        GPUAttachmentLayout attachmentLayout,
        uint width,
        uint height,
        string name = "unmaned_render_texture"
    )
    {
        GPUFrameBuffer frameBuffer = CreateFrameBuffer(
            attachmentLayout,
            width,
            height,
            name
        );

        return new RenderTexture(
            this,
            frameBuffer
        );
    }

    private GPUFrameBuffer CreateFrameBuffer(
        GPUAttachmentLayout attachmentLayout,
        uint width,
        uint height,
        string name
    )
    {
        FrameBufferDescriptor descriptor = new FrameBufferDescriptor(
            attachmentLayout,
            width,
            height,
            name
        );

        return _device.CreateFrameBuffer(descriptor);
    }
}