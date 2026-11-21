// Independent Win32 DXGI swapchains, borrowed backbuffers, and queue-zero presentation retirement.

using System.Diagnostics;
using System.Runtime.InteropServices;
using static D3D12.D3D12Native;

using Alco.Graphics;

namespace Alco.Graphics.LowLevel;

internal sealed unsafe partial class D3D12Swapchain : GPUSwapchain
{
    private const uint MaxImages = 8;
    internal D3D12Device? Owner;
    internal nint Native;
    internal D3D12CommandBuffer? AcquireCommands;
    internal bool Acquired;
    internal uint ImageIndex;
    internal ulong LastFrameValue;
    internal string? InitializationFailure;
    internal Error Lost;

    private readonly D3D12Texture?[] Textures = new D3D12Texture?[MaxImages];
    private readonly D3D12RenderView?[] RenderViews = new D3D12RenderView?[MaxImages];
    private readonly nint[] TailAllocators = new nint[MaxImages];
    private readonly nint[] TailCommands = new nint[MaxImages];
    private readonly ulong[] ImageRetirement = new ulong[MaxImages];
    private nint Window;
    private uint WindowThread;
    private nint CompletionEvent;
    private nint RenderHeap;
    private uint ImageCount;
    private uint BufferFormat;
    private uint Flags;
    private Extent2D BufferExtent;
    private Extent2D RequestedExtent;
    private Format CurrentFormat;
    private ColorSpace CurrentColorSpace;
    private bool CurrentVSync;
    private bool TearingSupported;
    private bool BuffersReady;
    private bool Registered;

    /// <summary>The current framebuffer extent, or zero while suspended or unavailable.</summary>
    public override Extent2D DrawableExtent => Lost == Error.None && RequestedExtent.X != 0 && RequestedExtent.Y != 0 ? RequestedExtent : default;

    /// <summary>The logical presentation format, including sRGB render-view encoding.</summary>
    public override Format Format => CurrentFormat;

    /// <summary>The native presentation color space.</summary>
    public override ColorSpace ColorSpace => CurrentColorSpace;

    /// <summary>Whether presentation is synchronized to the display refresh.</summary>
    public override bool VSync => CurrentVSync;

    /// <summary>Cancels the acquired frame and resizes after this swapchain's submitted GPU use completes.</summary>
    /// <param name="extent">The framebuffer extent; a zero dimension suspends acquisition.</param>
    /// <param name="error">None on success, or the configuration error.</param>
    /// <returns>true if the requested extent was applied; otherwise false.</returns>
    public override bool TryResize(Extent2D extent, out Error error)
    {
        error = Configure(extent, CurrentFormat, CurrentColorSpace);
        return error == Error.None;
    }

    /// <summary>Cancels the acquired frame and selects an exact native format and color space without fallback.</summary>
    /// <param name="format">The logical presentation format; Undefined selects Bgra8Unorm for Srgb or Rgba16Float for ExtendedSrgbLinear.</param>
    /// <param name="colorSpace">sRGB for 8-bit formats, or extended linear sRGB for RGBA16 float.</param>
    /// <param name="error">None on success, or the configuration error.</param>
    /// <returns>true if the requested configuration was applied; otherwise false.</returns>
    public override bool TrySetFormat(Format format, ColorSpace colorSpace, out Error error)
    {
        error = Configure(RequestedExtent, format, colorSpace);
        return error == Error.None;
    }

    /// <summary>Cancels the acquired frame and changes synchronization; unsynchronized presentation requires native tearing support.</summary>
    /// <param name="enabled">Whether to synchronize presentation to the display refresh.</param>
    /// <param name="error">None on success, or Unsupported if tearing is unavailable.</param>
    /// <returns>true if the requested mode was applied; otherwise false.</returns>
    public override bool TrySetVSync(bool enabled, out Error error)
    {
        Debug.Assert(Owner != null && WindowThread == SwapchainCurrentThreadId());
        error = Lost;
        if (error != Error.None)
            return false;
        if (!enabled && !TearingSupported)
        {
            error = Error.Unsupported;
            return false;
        }
        WaitRetirement(LastFrameValue);
        CancelAcquired();
        CurrentVSync = enabled;
        return true;
    }

    internal Error Initialize(in SwapchainDesc desc)
    {
        D3D12Device device = Owner!;
        Window = desc.Surface.Handle;
        WindowThread = SwapchainWindowThreadId(Window, null);
        if (WindowThread == 0 || WindowThread != SwapchainCurrentThreadId())
            return Unsupported("DXGI swapchains must be created and used on their Win32 window's message-pump thread.");
        Error error = ResolveFormat(desc.Format, desc.ColorSpace, out Format format, out uint nativeFormat, out _);
        if (error != Error.None)
            return error;
        error = CheckResult(SwapchainQueryInterface(device.Factory, SwapchainFactoryInterface, out nint factory), "IDXGIFactory5 QueryInterface");
        if (error != Error.None)
            return error;
        int result = SwapchainCheckTearing(factory, out TearingSupported);
        Release(factory);
        error = CheckResult(result, "IDXGIFactory5.CheckFeatureSupport (present tearing)");
        if (error != Error.None)
            return error;
        if (!desc.VSync && !TearingSupported)
            return Unsupported("Unsynchronized DXGI presentation requires native present-tearing support.");
        ImageCount = Math.Max(2u, desc.DesiredImageCount);
        Flags = TearingSupported ? SwapchainAllowTearing : 0;
        BufferExtent = desc.Extent.X == 0 || desc.Extent.Y == 0 ? new Extent2D(1, 1) : desc.Extent;
        BufferFormat = nativeFormat;
        RequestedExtent = desc.Extent;
        CurrentFormat = format;
        CurrentColorSpace = desc.ColorSpace;
        CurrentVSync = desc.VSync;
        SwapchainDesc1 nativeDesc = new()
        {
            Width = BufferExtent.X,
            Height = BufferExtent.Y,
            Format = nativeFormat,
            SampleCount = 1,
            BufferUsage = 0x20,
            BufferCount = ImageCount,
            SwapEffect = 4,
            AlphaMode = 3,
            Flags = Flags,
        };
        error = CheckResult(SwapchainCreateForHwnd(device.Factory, device.Queues[0].Native, Window, in nativeDesc, out nint swapchain),
            "IDXGIFactory2.CreateSwapChainForHwnd");
        if (error != Error.None)
            return error;
        result = SwapchainQueryInterface(swapchain, SwapchainInterface, out Native);
        Release(swapchain);
        error = CheckResult(result, "IDXGISwapChain3 QueryInterface");
        if (error != Error.None)
            return error;
        error = SetColorSpace(desc.ColorSpace);
        if (error != Error.None)
            return error;
        CompletionEvent = SwapchainCreateEvent(null, 0, 0, null);
        if (CompletionEvent == 0)
        {
            InitializationFailure = $"CreateEventW (swapchain retirement) failed (Win32 error {Marshal.GetLastPInvokeError()}).";
            return Error.DriverError;
        }
        error = CheckResult(SwapchainCreateRenderHeap(device.Native, ImageCount, out RenderHeap), "ID3D12Device.CreateDescriptorHeap (swapchain RTVs)");
        if (error != Error.None)
            return error;
        error = LoadBuffers(format, BufferExtent);
        if (error != Error.None)
            return error;
        error = CheckResult(SwapchainMakeWindowAssociation(device.Factory, Window, SwapchainNoAltEnter), "IDXGIFactory.MakeWindowAssociation");
        if (error != Error.None)
            return error;
        Interlocked.Increment(ref device.SwapchainCount);
        Registered = true;
        return Error.None;
    }

    private Error Unsupported(string message)
    {
        if (!Registered)
            InitializationFailure = message;
        return Error.Unsupported;
    }

    private Error ResolveFormat(Format requested, ColorSpace colorSpace, out Format format, out uint nativeFormat, out uint viewFormat)
    {
        format = requested == Format.Undefined
            ? colorSpace == ColorSpace.ExtendedSrgbLinear ? Format.Rgba16Float : Format.Bgra8Unorm
            : requested;
        nativeFormat = 0;
        viewFormat = 0;
        if (colorSpace == ColorSpace.Srgb)
        {
            switch (format)
            {
                case Format.Bgra8Unorm: nativeFormat = viewFormat = 87; break;
                case Format.Bgra8Srgb: nativeFormat = 87; viewFormat = 91; break;
                case Format.Rgba8Unorm: nativeFormat = viewFormat = 28; break;
                case Format.Rgba8Srgb: nativeFormat = 28; viewFormat = 29; break;
            }
        }
        else if (colorSpace == ColorSpace.ExtendedSrgbLinear && format == Format.Rgba16Float)
        {
            nativeFormat = viewFormat = 10;
        }
        return nativeFormat != 0 ? Error.None : Unsupported("The requested DXGI swapchain format and color space are unsupported.");
    }

    private Error SetColorSpace(ColorSpace colorSpace)
    {
        uint nativeColorSpace = colorSpace == ColorSpace.ExtendedSrgbLinear ? 1u : 0u;
        Error error = CheckResult(SwapchainCheckColorSpaceSupport(Native, nativeColorSpace, out uint support), "IDXGISwapChain3.CheckColorSpaceSupport");
        if (error != Error.None)
            return error;
        if ((support & 1) == 0)
            return Unsupported("The DXGI surface cannot present the requested color space.");
        return CheckResult(SwapchainSetColorSpace(Native, nativeColorSpace), "IDXGISwapChain3.SetColorSpace1");
    }

    private Error Configure(Extent2D extent, Format requested, ColorSpace colorSpace)
    {
        Debug.Assert(Owner != null && WindowThread == SwapchainCurrentThreadId());
        if (Lost != Error.None)
            return Lost;
        Error error = ResolveFormat(requested, colorSpace, out Format format, out uint nativeFormat, out uint viewFormat);
        if (error != Error.None)
            return error;
        WaitRetirement(LastFrameValue);
        CancelAcquired();
        Extent2D nativeExtent = extent.X == 0 || extent.Y == 0 ? BufferExtent : extent;
        if (nativeFormat != BufferFormat || nativeExtent.X != BufferExtent.X || nativeExtent.Y != BufferExtent.Y)
        {
            ReleaseBuffers();
            error = CheckResult(SwapchainResizeBuffers(Native, ImageCount, nativeExtent.X, nativeExtent.Y, nativeFormat, Flags),
                "IDXGISwapChain.ResizeBuffers");
            if (error != Error.None)
            {
                if (Lost == Error.None)
                {
                    Error restored = LoadBuffers(CurrentFormat, BufferExtent);
                    if (restored != Error.None)
                        Lost = restored == Error.DeviceLost ? Error.DeviceLost : Error.DriverError;
                }
                return error;
            }
            error = SetColorSpace(colorSpace);
            if (error == Error.None)
                error = LoadBuffers(format, nativeExtent);
            if (error != Error.None)
            {
                RestoreBuffers();
                return error;
            }
            BufferExtent = nativeExtent;
            BufferFormat = nativeFormat;
        }
        else if (format != CurrentFormat)
        {
            nuint start = SwapchainRenderHeapStart(RenderHeap);
            for (uint index = 0; index < ImageCount; ++index)
            {
                D3D12Texture texture = Textures[index]!;
                texture.Desc.Format = format;
                SwapchainCreateRenderView(Owner!.Native, texture.Native, viewFormat, start + index * Owner.RenderDescriptorSize);
            }
        }
        RequestedExtent = extent;
        CurrentFormat = format;
        CurrentColorSpace = colorSpace;
        return Error.None;
    }

    private void RestoreBuffers()
    {
        ReleaseBuffers();
        if (Lost != Error.None)
            return;
        Error error = CheckResult(SwapchainResizeBuffers(Native, ImageCount, BufferExtent.X, BufferExtent.Y, BufferFormat, Flags),
            "IDXGISwapChain.ResizeBuffers (rollback)");
        if (error == Error.None)
            error = SetColorSpace(CurrentColorSpace);
        if (error == Error.None)
            error = LoadBuffers(CurrentFormat, BufferExtent);
        if (error != Error.None)
        {
            Lost = error == Error.DeviceLost ? Error.DeviceLost : Error.DriverError;
            Gpu.ReportError("DXGI swapchain configuration rollback failed; destroy and recreate this swapchain.", false);
        }
    }

    private Error LoadBuffers(Format format, Extent2D extent)
    {
        D3D12Device device = Owner!;
        _ = ResolveFormat(format, format == Format.Rgba16Float ? ColorSpace.ExtendedSrgbLinear : ColorSpace.Srgb, out _, out _, out uint viewFormat);
        nuint start = SwapchainRenderHeapStart(RenderHeap);
        for (uint index = 0; index < ImageCount; ++index)
        {
            Error error = CheckResult(SwapchainGetBuffer(Native, index, out nint resource), "IDXGISwapChain.GetBuffer");
            if (error != Error.None)
                return error;
            D3D12Texture texture = new()
            {
                Owner = device,
                Native = resource,
                Swapchain = this,
                Desc = new TextureDesc
                {
                    Type = TextureType.TwoD,
                    Extent = new Extent3D(extent.X, extent.Y, 1),
                    MipLevels = 1,
                    LayerCount = 1,
                    Format = format,
                    Usage = TextureUsage.ColorAttachment | TextureUsage.TransferSource | TextureUsage.TransferDestination,
                },
            };
            Textures[index] = texture;
            nuint handle = start + index * device.RenderDescriptorSize;
            SwapchainCreateRenderView(device.Native, resource, viewFormat, handle);
            RenderViews[index] = new D3D12RenderView
            {
                Owner = device,
                Texture = texture,
                Handle = handle,
                Width = extent.X,
                Height = extent.Y,
                SwapchainView = true,
            };
            error = CheckResult(SwapchainCreateAllocator(device.Native, out TailAllocators[index]), "ID3D12Device.CreateCommandAllocator (presentation)");
            if (error != Error.None)
                return error;
            error = CheckResult(SwapchainCreateCommands(device.Native, TailAllocators[index], out TailCommands[index]),
                "ID3D12Device.CreateCommandList (presentation)");
            if (error != Error.None)
                return error;
            RecordPresent(TailCommands[index], resource);
            error = CheckResult(SwapchainCloseCommands(TailCommands[index]), "ID3D12GraphicsCommandList.Close (presentation)");
            if (error != Error.None)
                return error;
        }
        BuffersReady = true;
        return Error.None;
    }

    private static void RecordPresent(nint commands, nint resource)
    {
        // PRESENT and COMMON alias layout zero; this standalone list has no execution or access scope.
        CommandTextureBarrier barrier = new()
        {
            AccessBefore = CommandNoAccess,
            AccessAfter = CommandNoAccess,
            Resource = resource,
            Subresources = new CommandSubresources { MipCount = 1, SliceCount = 1, PlaneCount = 1 },
        };
        CommandBarrierGroup group = new() { Type = 1, Count = 1, Barriers = &barrier };
        CommandBarriers(commands, &group);
    }

    private void ReleaseBuffers()
    {
        BuffersReady = false;
        for (uint index = 0; index < ImageCount; ++index)
        {
            Release(TailCommands[index]);
            Release(TailAllocators[index]);
            TailCommands[index] = 0;
            TailAllocators[index] = 0;
            if (RenderViews[index] != null)
            {
                RenderViews[index]!.Handle = 0;
                RenderViews[index] = null;
            }
            if (Textures[index] != null)
            {
                Release(Textures[index]!.Native);
                Textures[index]!.Native = 0;
                Textures[index] = null;
            }
            ImageRetirement[index] = 0;
        }
    }

    internal SwapchainFrame Acquire(D3D12CommandBuffer commands)
    {
        Debug.Assert(Owner == commands.Owner && commands.Pool.QueueIndex == 0 && !Acquired && commands.Swapchain == null);
        Debug.Assert(WindowThread == SwapchainCurrentThreadId());
        if (Lost != Error.None || !BuffersReady || RequestedExtent.X == 0 || RequestedExtent.Y == 0)
            return default;
        uint image = SwapchainGetCurrentBackBufferIndex(Native);
        Debug.Assert(image < ImageCount);
        WaitRetirement(ImageRetirement[image]);
        CommandTextureBarrier barrier = new()
        {
            SyncAfter = CommandSyncAll,
            AccessBefore = CommandNoAccess,
            Resource = Textures[image]!.Native,
            Subresources = new CommandSubresources { MipCount = 1, SliceCount = 1, PlaneCount = 1 },
        };
        commands.RecordTextureBarriers(&barrier, 1);
        ImageIndex = image;
        AcquireCommands = commands;
        commands.Swapchain = this;
        commands.UseSwapchain(this);
        Acquired = true;
        return new SwapchainFrame { Texture = Textures[image], RenderView = RenderViews[image], Extent = RequestedExtent };
    }

    internal void PreparePresent()
    {
        Debug.Assert(Owner != null && Acquired && AcquireCommands == null && BuffersReady);
        Debug.Assert(WindowThread == SwapchainCurrentThreadId());
        SwapchainExecuteTail(Owner.Queues[0].Native, TailCommands[ImageIndex]);
    }

    internal void Present()
    {
        Debug.Assert(Acquired && AcquireCommands == null && WindowThread == SwapchainCurrentThreadId());
        int result = SwapchainPresent(Native, CurrentVSync ? 1u : 0u, CurrentVSync ? 0u : SwapchainPresentAllowTearing);
        Acquired = false;
        if (result >= 0)
            return;
        if (result == SwapchainAccessLost || (result == SwapchainInvalidCall && SwapchainIsWindow(Window) == 0))
        {
            _ = CheckResult(result, "IDXGISwapChain.Present");
            return;
        }
        AbortExecution(result, "IDXGISwapChain.Present");
    }

    internal void MarkSubmitted(ulong value)
    {
        Debug.Assert(value >= LastFrameValue);
        LastFrameValue = value;
        ImageRetirement[ImageIndex] = value;
    }

    private void CancelAcquired()
    {
        if (AcquireCommands != null)
        {
            AcquireCommands.Swapchain = null;
            AcquireCommands = null;
        }
        Acquired = false;
    }

    private void WaitRetirement(ulong value)
    {
        if (value == 0)
            return;
        nint fence = Owner!.Queues[0].Completion;
        ulong completed = SwapchainCompletedValue(fence);
        if (completed == ulong.MaxValue)
            AbortExecution(SwapchainDeviceRemoved, "ID3D12Fence.GetCompletedValue (swapchain retirement)");
        if (completed >= value)
            return;
        int result = SwapchainSetCompletionEvent(fence, value, CompletionEvent);
        if (result < 0)
            AbortExecution(result, "ID3D12Fence.SetEventOnCompletion (swapchain retirement)");
        if (SwapchainWait(CompletionEvent, uint.MaxValue) != 0)
            AbortExecution(Marshal.GetHRForLastWin32Error(), "WaitForSingleObject (swapchain retirement)");
        if (SwapchainCompletedValue(fence) == ulong.MaxValue)
            AbortExecution(SwapchainDeviceRemoved, "ID3D12Fence.GetCompletedValue (swapchain retirement)");
    }

    private static bool IsDeviceLost(int result) => result == SwapchainDeviceRemoved || result == SwapchainDeviceHung || result == SwapchainDeviceReset;

    private Error CheckResult(int result, string operation)
    {
        if (result >= 0)
            return Error.None;
        Error error = IsDeviceLost(result) ? Error.DeviceLost : result switch
        {
            unchecked((int)0x80004002) or unchecked((int)0x80070057) or unchecked((int)0x887A0004) => Error.Unsupported,
            unchecked((int)0x8007000E) => Error.OutOfMemory,
            _ => Error.DriverError,
        };
        if (error == Error.DeviceLost || result == SwapchainAccessLost || (result == SwapchainInvalidCall && SwapchainIsWindow(Window) == 0))
            Lost = error;
        string message = $"{operation} failed (HRESULT 0x{(uint)result:X8}).";
        if (!Registered)
            InitializationFailure = message;
        else
            Gpu.ReportError(message, false);
        return error;
    }

    private static void AbortExecution(int result, string operation)
    {
        string message = $"{operation} failed (HRESULT 0x{(uint)result:X8}).";
        Gpu.ReportError(message, true);
        Debug.Assert(false, message);
        Environment.FailFast(message);
    }

    /// <summary>Cancels acquisition, drains only this swapchain's submitted work, and releases its native presentation resources.</summary>
    public override void Destroy()
    {
        D3D12Device device = Owner!;
        Debug.Assert(device != null && (Native == 0 || WindowThread == SwapchainCurrentThreadId()));
        WaitRetirement(LastFrameValue);
        CancelAcquired();
        ReleaseBuffers();
        Release(RenderHeap);
        Release(Native);
        if (CompletionEvent != 0)
            _ = SwapchainCloseHandle(CompletionEvent);
        RenderHeap = 0;
        Native = 0;
        CompletionEvent = 0;
        if (Registered)
            Interlocked.Decrement(ref device.SwapchainCount);
        Owner = null;
    }
}

internal sealed unsafe partial class D3D12Device
{
    /// <summary>Creates an independent, queue-zero DXGI flip-discard swapchain for a Win32 window.</summary>
    /// <param name="desc">The native window and exact presentation configuration.</param>
    /// <returns>The initialized swapchain.</returns>
    public override GPUSwapchain CreateSwapchain(in SwapchainDesc desc)
    {
        if (!PresentationEnabled)
            throw new GPUException(Error.Unsupported, "Create a D3D12 device with Headless=false before creating swapchains.");
        if (desc.Surface.Kind != SurfaceKind.Win32 || desc.Surface.Handle == 0)
            throw new GPUException(Error.Unsupported, "DXGI presentation requires a Win32 HWND surface.");
        Debug.Assert(desc.DesiredImageCount >= 1 && desc.DesiredImageCount <= 8);
        if (desc.DesiredImageCount == 0 || desc.DesiredImageCount > 8)
            throw new GPUException(Error.Unsupported, "The requested DXGI swapchain image count must be between one and eight.");
        D3D12Swapchain swapchain = new() { Owner = this };
        Error error;
        bool initialized = false;
        try
        {
            error = swapchain.Initialize(in desc);
            initialized = error == Error.None;
        }
        finally
        {
            if (!initialized)
                swapchain.Destroy();
        }
        if (error != Error.None)
            throw new GPUException(error, swapchain.InitializationFailure ?? "The requested DXGI swapchain configuration is unsupported.");
        return swapchain;
    }
}
