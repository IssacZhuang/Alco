using Alco.Graphics.NoGPU;
using NUnit.Framework;

namespace Alco.Graphics.Test;

/// <summary>
/// Verifies the stage-0 RHI additions with the NoGPU backend: attachment load/store ops,
/// batch submission and external frame buffer validation.
/// </summary>
[TestFixture]
public sealed class AttachmentOpsTests
{
    [Test(Description = "BeginRender accepts attachment load/store ops without throwing")]
    public void BeginRenderAcceptsAttachmentOps()
    {
        NoDevice device = NoDevice.noDevice;
        GPUAttachmentLayout layout = device.CreateAttachmentLayout(new AttachmentLayoutDescriptor(
            [new ColorAttachment(PixelFormat.RGBA8Unorm)],
            new DepthAttachment(PixelFormat.Depth32Float),
            "attachment_ops_test"));
        GPUFrameBuffer frameBuffer = device.CreateFrameBuffer(new FrameBufferDescriptor(layout, 16, 16, "attachment_ops_test"));
        GPUCommandBuffer commandBuffer = device.CreateCommandBuffer("attachment_ops_test");

        commandBuffer.Begin();
        AttachmentOps[] colorOps = [new AttachmentOps { LoadOp = AttachmentLoadOp.Clear, StoreOp = AttachmentStoreOp.Discard }];
        using (commandBuffer.BeginRender(
            frameBuffer,
            ReadOnlySpan<ClearColorData>.Empty,
            clearDepth: null,
            clearStencil: null,
            colorOps: colorOps,
            depthOps: new AttachmentOps { LoadOp = AttachmentLoadOp.Load, StoreOp = AttachmentStoreOp.Store }))
        {
        }
        commandBuffer.End();

        Assert.DoesNotThrow(() => device.Submit(commandBuffer));
    }

    [Test(Description = "CreateExternalFrameBuffer rejects a color count mismatch")]
    public void CreateExternalFrameBufferRejectsColorCountMismatch()
    {
        NoDevice device = NoDevice.noDevice;
        GPUAttachmentLayout layout = device.CreateAttachmentLayout(new AttachmentLayoutDescriptor(
            [new ColorAttachment(PixelFormat.RGBA8Unorm)],
            null,
            "external_fb_test"));
        GPUTexture textureA = CreateColorTexture(device);
        GPUTexture textureB = CreateColorTexture(device);
        GPUTextureView viewA = device.CreateTextureView(new TextureViewDescriptor(textureA));
        GPUTextureView viewB = device.CreateTextureView(new TextureViewDescriptor(textureB));

        ExternalFrameBufferDescriptor descriptor = new()
        {
            AttachmentLayout = layout,
            Colors = [textureA, textureB],
            ColorViews = [viewA, viewB],
            Width = 16,
            Height = 16,
        };

        Assert.Throws<GraphicsException>(() => device.CreateExternalFrameBuffer(descriptor));
    }

    [Test(Description = "CreateExternalFrameBuffer requires a depth stencil view when a depth texture is set")]
    public void CreateExternalFrameBufferRequiresDepthStencilView()
    {
        NoDevice device = NoDevice.noDevice;
        GPUAttachmentLayout layout = device.CreateAttachmentLayout(new AttachmentLayoutDescriptor(
            [new ColorAttachment(PixelFormat.RGBA8Unorm)],
            new DepthAttachment(PixelFormat.Depth32Float),
            "external_fb_test"));
        GPUTexture colorTexture = CreateColorTexture(device);
        GPUTextureView colorView = device.CreateTextureView(new TextureViewDescriptor(colorTexture));
        GPUTexture depthTexture = CreateDepthTexture(device);

        ExternalFrameBufferDescriptor descriptor = new()
        {
            AttachmentLayout = layout,
            Colors = [colorTexture],
            ColorViews = [colorView],
            DepthStencil = depthTexture,
            DepthStencilView = null,
            Width = 16,
            Height = 16,
        };

        Assert.Throws<GraphicsException>(() => device.CreateExternalFrameBuffer(descriptor));
    }

    [Test(Description = "CreateExternalFrameBuffer rejects a texture size mismatch")]
    public void CreateExternalFrameBufferRejectsSizeMismatch()
    {
        NoDevice device = NoDevice.noDevice;
        GPUAttachmentLayout layout = device.CreateAttachmentLayout(new AttachmentLayoutDescriptor(
            [new ColorAttachment(PixelFormat.RGBA8Unorm)],
            null,
            "external_fb_test"));
        GPUTexture colorTexture = CreateColorTexture(device);
        GPUTextureView colorView = device.CreateTextureView(new TextureViewDescriptor(colorTexture));

        ExternalFrameBufferDescriptor descriptor = new()
        {
            AttachmentLayout = layout,
            Colors = [colorTexture],
            ColorViews = [colorView],
            Width = 32,
            Height = 32,
        };

        Assert.Throws<GraphicsException>(() => device.CreateExternalFrameBuffer(descriptor));
    }

    [Test(Description = "A valid external frame buffer descriptor creates a frame buffer")]
    public void CreateExternalFrameBufferSucceeds()
    {
        NoDevice device = NoDevice.noDevice;
        GPUAttachmentLayout layout = device.CreateAttachmentLayout(new AttachmentLayoutDescriptor(
            [new ColorAttachment(PixelFormat.RGBA8Unorm)],
            new DepthAttachment(PixelFormat.Depth32Float),
            "external_fb_test"));
        GPUTexture colorTexture = CreateColorTexture(device);
        GPUTextureView colorView = device.CreateTextureView(new TextureViewDescriptor(colorTexture));
        GPUTexture depthTexture = CreateDepthTexture(device);
        GPUTextureView depthView = device.CreateTextureView(new TextureViewDescriptor(depthTexture));

        GPUFrameBuffer frameBuffer = device.CreateExternalFrameBuffer(new ExternalFrameBufferDescriptor
        {
            AttachmentLayout = layout,
            Colors = [colorTexture],
            ColorViews = [colorView],
            DepthStencil = depthTexture,
            DepthStencilView = depthView,
            Width = 16,
            Height = 16,
        });

        Assert.That(frameBuffer.Width, Is.EqualTo(16));
        Assert.That(frameBuffer.Height, Is.EqualTo(16));
        Assert.That(frameBuffer.Colors.Length, Is.EqualTo(1));
        Assert.That(frameBuffer.DepthStencil, Is.SameAs(depthTexture));
    }

    [Test(Description = "Submitting an empty command buffer span is a no-op")]
    public void SubmitEmptySpanIsNoOp()
    {
        NoDevice device = NoDevice.noDevice;
        Assert.DoesNotThrow(() => device.Submit(ReadOnlySpan<GPUCommandBuffer>.Empty));
    }

    [Test(Description = "Batch submit accepts recorded command buffers and rejects empty ones")]
    public void SubmitSpanValidatesRecordedBuffers()
    {
        NoDevice device = NoDevice.noDevice;
        GPUCommandBuffer recorded = device.CreateCommandBuffer("batch_submit_test");
        recorded.Begin();
        recorded.End();

        GPUCommandBuffer[] buffers = [recorded];
        Assert.DoesNotThrow(() => device.Submit(buffers));

        GPUCommandBuffer empty = device.CreateCommandBuffer("batch_submit_empty_test");
        GPUCommandBuffer[] emptyBuffers = [empty];
        Assert.Throws<GraphicsException>(() => device.Submit(emptyBuffers));
    }

    private static GPUTexture CreateColorTexture(NoDevice device)
    {
        return device.CreateTexture(new TextureDescriptor(
            TextureDimension.Texture2D,
            PixelFormat.RGBA8Unorm,
            16,
            16,
            usage: GPUFrameBuffer.ColorAttachmentUsage,
            name: "external_fb_color_texture"));
    }

    private static GPUTexture CreateDepthTexture(NoDevice device)
    {
        return device.CreateTexture(new TextureDescriptor(
            TextureDimension.Texture2D,
            PixelFormat.Depth32Float,
            16,
            16,
            usage: GPUFrameBuffer.DepthAttachmentUsage,
            name: "external_fb_depth_texture"));
    }
}
