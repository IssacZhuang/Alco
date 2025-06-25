using Alco.Graphics;

namespace Alco.Rendering;

// render texture factory

public partial class RenderingSystem
{
    /// <summary>
    /// Create a render texture with the given render pass, width, height and name.
    /// </summary>
    /// <param name="attachmentLayout"> The render pass to create the render texture. </param>
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
        return CreateRenderTexture(
            attachmentLayout,
            width,
            height,
            _device.SamplerLinearClamp,
            name
        );
    }

    /// <summary>
    /// Create a render texture with the given render pass, filter mode, width, height and name.
    /// </summary>
    /// <param name="attachmentLayout"> The render pass to create the render texture. </param>
    /// <param name="filterMode"> The filter mode to use for the sampler. </param>
    /// <param name="width"> The width of the render texture. </param>
    /// <param name="height"> The height of the render texture. </param>
    /// <param name="name"> The name of the render texture. </param>
    /// <returns></returns>
    public RenderTexture CreateRenderTexture(
        GPUAttachmentLayout attachmentLayout,
        uint width,
        uint height,
        FilterMode filterMode,
        string name = "unmaned_render_texture"
    )
    {
        GPUSampler sampler = _device.GetSampler(filterMode, AddressMode.ClampToEdge);
        return CreateRenderTexture(attachmentLayout, width, height, sampler, name);
    }

    /// <summary>
    /// Create a render texture with the given render pass, filter mode, address mode, width, height and name.
    /// </summary>
    /// <param name="attachmentLayout"> The render pass to create the render texture. </param>
    /// <param name="filterMode"> The filter mode to use for the sampler. </param>
    /// <param name="addressMode"> The address mode to use for the sampler. </param>
    /// <param name="width"> The width of the render texture. </param>
    /// <param name="height"> The height of the render texture. </param>
    /// <param name="name"></param>
    /// <returns></returns>
    public RenderTexture CreateRenderTexture(
        GPUAttachmentLayout attachmentLayout,
        uint width,
        uint height,
        FilterMode filterMode,
        AddressMode addressMode,
        string name = "unmaned_render_texture"
    )
    {
        GPUSampler sampler = _device.GetSampler(filterMode, addressMode);
        return CreateRenderTexture(attachmentLayout, width, height, sampler, name);
    }

    /// <summary>
    /// Create a render texture with the given render pass, sampler, width, height and name.
    /// </summary>
    /// <param name="attachmentLayout"> The render pass to create the render texture. </param>
    /// <param name="sampler"> The sampler to use for the render texture. </param>
    /// <param name="width"> The width of the render texture. </param>
    /// <param name="height"> The height of the render texture. </param>
    /// <param name="name"> The name of the render texture. </param>
    /// <returns></returns>
    public RenderTexture CreateRenderTexture(
        GPUAttachmentLayout attachmentLayout,
        uint width,
        uint height,
        GPUSampler sampler,
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
            frameBuffer,
            sampler
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