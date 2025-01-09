using Vocore.Graphics;

namespace Vocore.Rendering;

public partial class RenderingSystem
{
    /// <summary>
    /// Create a render texture with the given render pass, width, height and name.
    /// </summary>
    /// <param name="renderPass"> The render pass to create the render texture. </param>
    /// <param name="width"> The width of the render texture. </param>
    /// <param name="height"> The height of the render texture. </param>
    /// <param name="name"> The name of the render texture. </param>
    /// <returns></returns>
    public RenderTexture CreateRenderTexture(
        GPURenderPass renderPass,
        uint width,
        uint height,
        string name = "unmaned_render_texture"
    )
    {
        GPUFrameBuffer frameBuffer = CreateFrameBuffer(
            renderPass,
            width,
            height,
            name
        );

        return new RenderTexture(
            _device,
            frameBuffer
        );
    }

    private GPUFrameBuffer CreateFrameBuffer(
        GPURenderPass renderPass,
        uint width,
        uint height,
        string name
    )
    {
        FrameBufferDescriptor descriptor = new FrameBufferDescriptor(
            renderPass,
            width,
            height,
            name
        );

        return _device.CreateFrameBuffer(descriptor);
    }
}