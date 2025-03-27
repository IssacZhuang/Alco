using System;
using System.Runtime.InteropServices;
using Alco.Engine;
using Alco.Graphics;
using Avalonia;
using Avalonia.Controls;

using Avalonia.Platform;

using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.Rendering.SceneGraph;


namespace Alco.Editor.Views;

public unsafe partial class GPUSurfaceView : NativeControlHost, IEngineSystem
{
    private readonly GPUDevice _device;
    private readonly GPUCommandBuffer _commandBuffer;
    private GPUSwapchain? _swapchain;

    private int _frameRate = 60;

    private GPUSwapchain Swapchain
    {
        get
        {
            _swapchain ??= CreateSwapchain();
            return _swapchain;
        }
    }

    public IntPtr Handle { get; private set; }

    public int Order => 0;

    public GPUSurfaceView()
    {
        GameEngine engine = App.Main.Engine;
        _device = engine.Rendering.GraphicsDevice;
        _commandBuffer = _device.CreateCommandBuffer( "GPUSurfaceView_CommandBuffer");
        
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        App.Main.Engine.AddSystem(this);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        App.Main.Engine.RemoveSystem(this);
    }


    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        var handle = base.CreateNativeControlCore(parent);
        Handle = handle.Handle;

        Log.Info($"Native window handle created: {Handle:X}");

        return handle;
    }

    private void OnRenderCore(GPUFrameBuffer frameBuffer)
    {
        OnRender(frameBuffer, 1f / _frameRate);
    }

    private GPUSwapchain CreateSwapchain()
    {
        var bounds = Bounds;
        uint width = math.max(1, (uint)bounds.Width);
        uint height = math.max(1, (uint)bounds.Height);

        var swapchain = _device.CreateSwapchain(
            new SwapchainDescriptor(
                SurfaceSource.CreateWin32Window(Handle, GetModuleHandleW(null)),
                _device.PrefferedSurfaceFomat,
                null,
                width,
                height,
                true));

        Log.Success($"Swapchain created with size {width}x{height} in {_device.PrefferedSurfaceFomat}");
        return swapchain;
    }

    protected virtual void OnRender(GPUFrameBuffer frameBuffer, float deltaTime)
    {
        _commandBuffer.Begin();
        _commandBuffer.SetFrameBuffer(frameBuffer);
        _commandBuffer.ClearColor(new ColorFloat(0, 0, 0, 1));
        _commandBuffer.End();
        _device.Submit(_commandBuffer);
    }


    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        if (_swapchain == null || _device == null) return;
        var bounds = Bounds;
        uint width = math.max(1, (uint)bounds.Width);
        uint height = math.max(1, (uint)bounds.Height);

        // Resize the swapchain

        _swapchain?.Resize(width, height);

        Log.Success($"Resize swapchain to {width}x{height}");
    }



    public void Dispose()
    {
        // just let GC to finalize the objects
        // _swapchain?.Dispose();
        // _commandBuffer?.Dispose();
    }

    [LibraryImport("kernel32")]
    private static partial nint GetModuleHandleW(ushort* lpModuleName);

    public void OnStart()
    {
        
    }

    public void OnTick(float delta)
    {
        
    }

    public void OnPostTick(float delta)
    {
        
    }

    public void OnUpdate(float delta)
    {
        if(!Swapchain.RequestSurfaceTexture())
        {
            return;
        }
        OnRenderCore(Swapchain.FrameBuffer);
        Swapchain.Present();
    }

    public void OnPostUpdate(float delta)
    {
        
    }

    public void OnBeginFrame(float deltaTime)
    {
        
    }

    public void OnEndFrame(float deltaTime)
    {
        
    }

    public void OnStop()
    {
        
    }
}