using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// A pooled attachment texture with its baked views: the attachment view used as a
/// render pass target, plus depth/stencil sampling views for depth formats (mirroring
/// the view set of <see cref="GPUFrameBuffer"/>). Created once when the pool
/// materializes the texture and reused for every composed frame buffer; the views are
/// therefore never recreated on aliasing churn.
/// </summary>
internal sealed class PooledAttachment : System.IDisposable
{
    /// <summary>The pooled texture.</summary>
    internal readonly GPUTexture Texture;

    /// <summary>The view used as the color attachment, or as the depth-stencil attachment for depth formats.</summary>
    internal readonly GPUTextureView AttachmentView;

    /// <summary>The depth-only sampling view, or null for color formats.</summary>
    internal readonly GPUTextureView? DepthView;

    /// <summary>The stencil-only sampling view, or null when the format has no stencil.</summary>
    internal readonly GPUTextureView? StencilView;

    private PooledAttachment(
        GPUTexture texture,
        GPUTextureView attachmentView,
        GPUTextureView? depthView,
        GPUTextureView? stencilView)
    {
        Texture = texture;
        AttachmentView = attachmentView;
        DepthView = depthView;
        StencilView = stencilView;
    }

    /// <summary>
    /// Materializes a pool entry for the given key on the device. Color formats get a
    /// single attachment view; depth formats additionally get depth (and stencil, when
    /// present) sampling views, matching the view layout of an owning frame buffer.
    /// </summary>
    internal static PooledAttachment Create(GPUDevice device, in TexturePoolKey key, string name)
    {
        TextureDescriptor descriptor = new TextureDescriptor(
            TextureDimension.Texture2D,
            key.Format,
            key.Width,
            key.Height,
            depthOrArrayLayer: 1,
            mipLevels: key.MipLevels,
            usage: key.Usage,
            sampleCount: 1,
            name: name);
        GPUTexture texture = device.CreateTexture(descriptor);

        bool isDepth = PixelFormatUtility.IsDepthFormat(key.Format);

        // Mirror the owning frame buffer's view set: color attachments use the
        // default (all-aspects) view, depth-stencil attachments use aspect None.
        GPUTextureView attachmentView = isDepth
            ? device.CreateTextureView(new TextureViewDescriptor(texture, aspect: TextureAspect.None, name: name + "_attachment"))
            : device.CreateTextureView(new TextureViewDescriptor(texture, name: name + "_attachment"));

        GPUTextureView? depthView = null;
        GPUTextureView? stencilView = null;
        if (isDepth)
        {
            depthView = device.CreateTextureView(
                new TextureViewDescriptor(texture, aspect: TextureAspect.DepthOnly, name: name + "_depth"));
            if (PixelFormatUtility.HasStencil(key.Format))
            {
                stencilView = device.CreateTextureView(
                    new TextureViewDescriptor(texture, aspect: TextureAspect.StencilOnly, name: name + "_stencil"));
            }
        }

        return new PooledAttachment(texture, attachmentView, depthView, stencilView);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        AttachmentView.Dispose();
        DepthView?.Dispose();
        StencilView?.Dispose();
        Texture.Dispose();
    }
}
