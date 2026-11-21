// Metal swapchains: independent CAMetalLayer ownership, borrowed drawable textures and views, and presentation configuration.

using System.Diagnostics;
using System.Threading;
using Mtl = Metal;

using Alco.Graphics;

namespace Alco.Graphics.LowLevel;

internal sealed class MetalSwapchain : GPUSwapchain
{
    internal MetalDevice Owner = null!;
    internal Mtl.CAMetalLayer Layer;
    internal Mtl.CAMetalDrawable Drawable;
    internal MetalRenderView DrawableView = null!;
    internal MetalTexture DrawableTexture = null!;
    internal Mtl.MTLCommandQueue PresentationQueue;
    internal Mtl.MTLSharedEvent PresentationCompletion;
    internal ulong PresentationValue;
    internal ulong LastFrameValue;
    internal MetalCommandBuffer? AcquiringCommands;
    internal bool DrawableNeedsSignal;
    internal Extent2D CurrentExtent;
    internal Format CurrentFormat;
    internal ColorSpace CurrentColorSpace;
    internal bool CurrentVSync;

    /// <summary>The current drawable extent in framebuffer pixels.</summary>
    public override Extent2D DrawableExtent => CurrentExtent;

    /// <summary>The actual presentation format.</summary>
    public override Format Format => CurrentFormat;

    /// <summary>The actual presentation color space.</summary>
    public override ColorSpace ColorSpace => CurrentColorSpace;

    /// <summary>Whether presentation is synchronized to the display refresh.</summary>
    public override bool VSync => CurrentVSync;

    /// <summary>Cancels the acquired frame and changes the framebuffer size after GPU use has completed.</summary>
    /// <param name="extent">The framebuffer size in pixels; a zero dimension suspends presentation.</param>
    /// <param name="error">None on success.</param>
    /// <returns>true if the extent was applied; otherwise false.</returns>
    public override bool TryResize(Extent2D extent, out Error error)
    {
        using Mtl.NSAutoreleasePool pool = new();
        Debug.Assert(Layer.NativePtr != 0 && Owner.MtlQueues[0].Completion.SignaledValue >= LastFrameValue);
        ReleaseDrawable();
        MetalInterop.SetLayerDrawableSize(Layer.NativePtr, new MtlCGSize { Width = extent.X, Height = extent.Y });
        CurrentExtent = extent;
        error = Error.None;
        return true;
    }

    /// <summary>Cancels the acquired frame and selects a supported format after GPU use has completed.</summary>
    /// <param name="format">BGRA8 unorm or sRGB; Undefined selects BGRA8 unorm.</param>
    /// <param name="colorSpace">The sRGB color space.</param>
    /// <param name="error">None on success, or Unsupported without changing the current configuration.</param>
    /// <returns>true if the configuration was applied; otherwise false.</returns>
    public override bool TrySetFormat(Format format, ColorSpace colorSpace, out Error error)
    {
        if (format == Format.Undefined) format = Format.Bgra8Unorm;
        if ((format != Format.Bgra8Unorm && format != Format.Bgra8Srgb) || colorSpace != ColorSpace.Srgb)
        {
            error = Error.Unsupported;
            return false;
        }
        using Mtl.NSAutoreleasePool pool = new();
        Debug.Assert(Layer.NativePtr != 0 && Owner.MtlQueues[0].Completion.SignaledValue >= LastFrameValue);
        ReleaseDrawable();
        Layer.PixelFormat = MetalInterop.PixelFormat(format);
        CurrentFormat = format;
        DrawableTexture.Desc.Format = format;
        CurrentColorSpace = colorSpace;
        error = Error.None;
        return true;
    }

    /// <summary>Cancels the acquired frame and changes display synchronization after GPU use has completed.</summary>
    /// <param name="enabled">Whether to synchronize presentation to the display refresh.</param>
    /// <param name="error">None on success.</param>
    /// <returns>true if the mode was applied; otherwise false.</returns>
    public override bool TrySetVSync(bool enabled, out Error error)
    {
        using Mtl.NSAutoreleasePool pool = new();
        Debug.Assert(Layer.NativePtr != 0 && Owner.MtlQueues[0].Completion.SignaledValue >= LastFrameValue);
        ReleaseDrawable();
        Layer.DisplaySyncEnabled = enabled;
        CurrentVSync = enabled;
        error = Error.None;
        return true;
    }

    /// <summary>Releases this swapchain and cancels its acquired frame after GPU use has completed.</summary>
    public override void Destroy()
    {
        using Mtl.NSAutoreleasePool pool = new();
        Debug.Assert(Layer.NativePtr != 0 && Owner.MtlQueues[0].Completion.SignaledValue >= LastFrameValue);
        ReleaseDrawable();
        Owner.MtlQueues[0].Handle.RemoveResidencySet(Layer.ResidencySet);
        PresentationCompletion.Dispose();
        PresentationQueue.Dispose();
        Layer.Dispose();
        PresentationCompletion = default;
        PresentationQueue = default;
        Layer = default;
        Interlocked.Decrement(ref Owner.SwapchainCount);
    }

    internal void ReleaseDrawable()
    {
        if (AcquiringCommands != null)
        {
            AcquiringCommands.AcquiredSwapchain = null;
            AcquiringCommands = null;
        }
        if (DrawableNeedsSignal) Owner.MtlQueues[0].Handle.SignalDrawable(Drawable);
        // The reusable handles borrow the drawable's texture and never release it separately.
        DrawableView.Texture = default;
        DrawableTexture.Native = default;
        Drawable.Dispose();
        Drawable = default;
        DrawableNeedsSignal = false;
    }
}

internal sealed unsafe partial class MetalDevice : GPUDevice
{
    /// <summary>Creates an independent swapchain for a CAMetalLayer.</summary>
    /// <param name="desc">The layer and presentation configuration.</param>
    /// <returns>The swapchain.</returns>
    public override GPUSwapchain CreateSwapchain(in SwapchainDesc desc)
    {
        if (Headless) throw MetalBackend.CreationFailure(default, "a headless device cannot create a swapchain", Error.Unsupported);
        if (desc.Surface.Kind != SurfaceKind.MetalLayer || desc.Surface.Handle == 0)
            throw MetalBackend.CreationFailure(default, "swapchain requires a CAMetalLayer surface", Error.Unsupported);
        Format format = desc.Format == Format.Undefined ? Format.Bgra8Unorm : desc.Format;
        if ((format != Format.Bgra8Unorm && format != Format.Bgra8Srgb) || desc.ColorSpace != ColorSpace.Srgb)
            throw MetalBackend.CreationFailure(default, $"unsupported swapchain format {format} or color space {desc.ColorSpace}", Error.Unsupported);
        using Mtl.NSAutoreleasePool pool = new();
        Debug.Assert(desc.DesiredImageCount >= 1 && desc.DesiredImageCount <= 8);
        MetalSwapchain swapchain = new()
        {
            Owner = this,
            CurrentExtent = desc.Extent,
            CurrentFormat = format,
            CurrentColorSpace = desc.ColorSpace,
            CurrentVSync = desc.VSync,
        };
        try
        {
            if (desc.SteamOverlayCompatibility)
            {
                swapchain.PresentationQueue = MtlDevice.NewCommandQueue();
                if (swapchain.PresentationQueue.NativePtr == 0) throw MetalBackend.CreationFailure(default, "presentation queue");
                swapchain.PresentationCompletion = MtlDevice.NewSharedEvent();
                if (swapchain.PresentationCompletion.NativePtr == 0) throw MetalBackend.CreationFailure(default, "presentation completion event");
            }
            swapchain.DrawableTexture = new MetalTexture
            {
                Owner = this,
                Swapchain = swapchain,
                Desc = new TextureDesc
                {
                    Type = TextureType.TwoD,
                    Extent = new Extent3D(desc.Extent.X, desc.Extent.Y, 1),
                    Format = format,
                    Usage = TextureUsage.ColorAttachment,
                },
            };
            swapchain.DrawableView = new MetalRenderView { Owner = this, Source = swapchain.DrawableTexture, Swapchain = swapchain, SwapchainView = true };
            swapchain.Layer = new Mtl.CAMetalLayer(MetalInterop.retain(desc.Surface.Handle));
            if (swapchain.Layer.NativePtr == 0) throw MetalBackend.CreationFailure(default, "swapchain layer");
            swapchain.Layer.Device = MtlDevice;
            swapchain.Layer.PixelFormat = MetalInterop.PixelFormat(format);
            swapchain.Layer.MaximumDrawableCount = desc.DesiredImageCount < 3 ? 2ul : 3ul;
            swapchain.Layer.FramebufferOnly = false;
            swapchain.Layer.DisplaySyncEnabled = desc.VSync;
            MetalInterop.SetLayerDrawableSize(swapchain.Layer.NativePtr, new MtlCGSize { Width = desc.Extent.X, Height = desc.Extent.Y });
            Mtl.MTLResidencySet residency = swapchain.Layer.ResidencySet;
            if (residency.NativePtr == 0) throw MetalBackend.CreationFailure(default, "swapchain residency set");
            MtlQueues[0].Handle.AddResidencySet(residency);
            Interlocked.Increment(ref SwapchainCount);
            return swapchain;
        }
        catch (GPUException)
        {
            swapchain.PresentationCompletion.Dispose();
            swapchain.PresentationQueue.Dispose();
            swapchain.Layer.Dispose();
            throw;
        }
    }
}
