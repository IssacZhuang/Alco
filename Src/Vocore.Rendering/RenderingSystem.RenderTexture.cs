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
        return CreateRenderTexture(
            renderPass,
            _device.SamplerLinearClamp,
            width,
            height,
            name
        );
    }

    /// <summary>
    /// Create a render texture with the given render pass, filter mode, width, height and name.
    /// </summary>
    /// <param name="renderPass"> The render pass to create the render texture. </param>
    /// <param name="filterMode"> The filter mode to use for the sampler. </param>
    /// <param name="width"> The width of the render texture. </param>
    /// <param name="height"> The height of the render texture. </param>
    /// <param name="name"> The name of the render texture. </param>
    /// <returns></returns>
    public RenderTexture CreateRenderTexture(
        GPURenderPass renderPass,
        FilterMode filterMode,
        uint width,
        uint height,
        string name = "unmaned_render_texture"
    )
    {
        GPUSampler sampler = _device.GetSampler(filterMode, AddressMode.ClampToEdge);
        return CreateRenderTexture(renderPass, sampler, width, height, name);
    }

    /// <summary>
    /// Create a render texture with the given render pass, filter mode, address mode, width, height and name.
    /// </summary>
    /// <param name="renderPass"> The render pass to create the render texture. </param>
    /// <param name="filterMode"> The filter mode to use for the sampler. </param>
    /// <param name="addressMode"> The address mode to use for the sampler. </param>
    /// <param name="width"> The width of the render texture. </param>
    /// <param name="height"> The height of the render texture. </param>
    /// <param name="name"></param>
    /// <returns></returns>
    public RenderTexture CreateRenderTexture(
        GPURenderPass renderPass,
        FilterMode filterMode,
        AddressMode addressMode,
        uint width,
        uint height,
        string name = "unmaned_render_texture"
    )
    {
        GPUSampler sampler = _device.GetSampler(filterMode, addressMode);
        return CreateRenderTexture(renderPass, sampler, width, height, name);
    }

    /// <summary>
    /// Create a render texture with the given render pass, min filter, mag filter, mip filter, address mode U, V and W, width, height and name.
    /// </summary>
    /// <param name="renderPass"> The render pass to create the render texture. </param>
    /// <param name="minFilter"> The min filter to use for the sampler. </param>
    /// <param name="magFilter"> The mag filter to use for the sampler. </param>
    /// <param name="mipFilter"> The mip filter to use for the sampler. </param>
    /// <param name="addressModeU"> The address mode U to use for the sampler. </param>
    /// <param name="addressModeV"> The address mode V to use for the sampler. </param>
    /// <param name="addressModeW"> The address mode W to use for the sampler. </param>
    /// <param name="width"> The width of the render texture. </param>
    /// <param name="height"> The height of the render texture. </param>
    /// <param name="name"> The name of the render texture. </param>
    /// <returns></returns>
    public RenderTexture CreateRenderTexture(
        GPURenderPass renderPass,
        FilterMode minFilter,
        FilterMode magFilter,
        FilterMode mipFilter,
        AddressMode addressModeU,
        AddressMode addressModeV,
        AddressMode addressModeW,
        uint width,
        uint height,
        string name = "unmaned_render_texture"
    )
    {
        GPUSampler sampler = _device.GetSampler(
            minFilter,
            magFilter,
            mipFilter,
            addressModeU,
            addressModeV,
            addressModeW
        );
        return CreateRenderTexture(renderPass, sampler, width, height, name);
    }

    /// <summary>
    /// Create a render texture with the given render pass, sampler, width, height and name.
    /// </summary>
    /// <param name="renderPass"> The render pass to create the render texture. </param>
    /// <param name="sampler"> The sampler to use for the render texture. </param>
    /// <param name="width"> The width of the render texture. </param>
    /// <param name="height"> The height of the render texture. </param>
    /// <param name="name"> The name of the render texture. </param>
    /// <returns></returns>
    public RenderTexture CreateRenderTexture(
        GPURenderPass renderPass,
        GPUSampler sampler,
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
            frameBuffer,
            sampler
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