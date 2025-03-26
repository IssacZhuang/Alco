using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Alco.Engine;
using Alco.Graphics;
using Alco.Rendering;
using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Platform;
using Avalonia.Rendering.Composition;
using Avalonia.VisualTree;
using SDL3;
using static SDL3.SDL3;

namespace Alco.Editor.Views;

public unsafe partial class GPUSurfaceView : NativeControlHost, IDisposable
{
    private GPUDevice? _device;

    public IntPtr Handle { get; private set; }

    public GPUSwapchain? Swapchain { get; private set; }

    public GPUSurfaceView()
    {
    }

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        GameEngine engine = App.Main.Engine;
        _device = engine.Rendering.GraphicsDevice;

        var handle = base.CreateNativeControlCore(parent);
        Handle = handle.Handle;

        Log.Info($"Native window handle created: {Handle:X}, IsVisible: {IsVisible}");

        var bounds = Bounds;
        uint width = math.max(1, (uint)bounds.Width);
        uint height = math.max(1, (uint)bounds.Height);
        RenderingSystem renderingSystem = engine.Rendering;
        GPUDevice device = renderingSystem.GraphicsDevice;

        Swapchain = device.CreateSwapchain(
            new SwapchainDescriptor(
                SurfaceSource.CreateWin32Window(Handle, GetModuleHandleW(null)),
                device.PrefferedSurfaceFomat,
                null,
                width,
                height,
                true));

        Log.Success($"Swapchain created with size {width}x{height}");

        // Initial clear will be done in OnLoaded
        return handle;
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        if (Swapchain == null || _device == null) return;
        var bounds = Bounds;
        uint width = math.max(1, (uint)bounds.Width);
        uint height = math.max(1, (uint)bounds.Height);
        Swapchain.Resize(width, height);
        Log.Success($"Resize to {width}x{height}");

        Render();
    }

    private void Render()
    {
        if (Swapchain == null || _device == null) return;

        Swapchain.Present();
        GPUCommandBuffer commandBuffer = _device.CreateCommandBuffer();
        commandBuffer.Begin();
        commandBuffer.SetFrameBuffer(Swapchain.FrameBuffer);
        commandBuffer.ClearColor(new ColorFloat(0, 1, 1, 1));
        commandBuffer.End();
        _device.Submit(commandBuffer);
        Swapchain.Present();

        Log.Success("Clear color completed");
    }

    public void Dispose()
    {
        Swapchain?.Dispose();
    }

    [LibraryImport("kernel32")]
    private static partial nint GetModuleHandleW(ushort* lpModuleName);
}