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

public unsafe partial class GPUSurfaceView : NativeControlHost, IDisposable
{
    private class GPURenderOperation : ICustomDrawOperation
    {
        private readonly GPUSurfaceView _view;

        private readonly GPUSwapchain _swapchain;

        public GPURenderOperation(
            GPUSurfaceView view,
            GPUSwapchain swapchain
        )
        {
            _view = view;
            _swapchain = swapchain;
        }
        public Avalonia.Rect Bounds => _view.Bounds;

        public void Dispose()
        {
            //not the real dispose because it is a reusable object
        }

        public bool Equals(ICustomDrawOperation? other)
        {
            return other is GPURenderOperation operation && operation._view == _view;
        }

        public bool HitTest(Avalonia.Point p)
        {
            return _view.Bounds.Contains(p);
        }


        public void Render(ImmediateDrawingContext context)
        {
            _view.OnRenderCore(_swapchain.FrameBuffer);
            _swapchain.Present();
        }

    }

    private readonly GPUDevice _device;
    private readonly GPUCommandBuffer _commandBuffer;
    private GPURenderOperation? _renderOperation;
    
    private GPUSwapchain? _swapchain;


    private DispatcherTimer? _renderTimer;
    private int _frameRate = 60;
    public IntPtr Handle { get; private set; }
    public int FrameRate
    {
        get => _frameRate;
        set
        {
            _frameRate = value;
            if (_renderTimer != null)
            {
                _renderTimer.Interval = TimeSpan.FromSeconds(1.0f / _frameRate);
            }
        }
    }

    public GPUSurfaceView()
    {
        GameEngine engine = App.Main.Engine;
        _device = engine.Rendering.GraphicsDevice;
        _commandBuffer = _device.CreateCommandBuffer( "GPUSurfaceView_CommandBuffer");

        _frameRate = 60;
        
    }

    ~GPUSurfaceView()
    {
        Log.Info("GPUSurfaceView is being finalized");
        Dispose();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        StopRenderTimer();
        StartRenderTimer();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        StopRenderTimer();
    }

    private void StartRenderTimer()
    {
        _renderTimer = new DispatcherTimer();
        _renderTimer.Interval = TimeSpan.FromSeconds(1.0f / _frameRate);
        _renderTimer.Tick += (sender, e) =>
        {
            InvalidateVisual();
        };
        _renderTimer.Start();
    }

    private void StopRenderTimer()
    {
        _renderTimer?.Stop();
        _renderTimer = null;
    }

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        var handle = base.CreateNativeControlCore(parent);
        Handle = handle.Handle;

        Log.Info($"Native window handle created: {Handle:X}");

        var bounds = Bounds;
        uint width = math.max(1, (uint)bounds.Width);
        uint height = math.max(1, (uint)bounds.Height);

        _swapchain = _device.CreateSwapchain(
            new SwapchainDescriptor(
                SurfaceSource.CreateWin32Window(Handle, GetModuleHandleW(null)),
                _device.PrefferedSurfaceFomat,
                null,
                width,
                height,
                true));

        _renderOperation = new GPURenderOperation(this, _swapchain);

        Log.Success($"Swapchain created with size {width}x{height} in {_device.PrefferedSurfaceFomat}");

        return handle;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (_renderOperation == null) return;
        context.Custom(_renderOperation);
    }

    private void OnRenderCore(GPUFrameBuffer frameBuffer)
    {
        OnRender(frameBuffer, 1f / _frameRate);
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

        _swapchain.Resize(width, height);
        // Force an immediate render to apply the resize
        InvalidateVisual();

        // Force an immediate render to apply the resize
        InvalidateVisual();

        Log.Success($"Resize swapchain to {width}x{height}");
    }



    public void Dispose()
    {
        _swapchain?.Dispose();
        _commandBuffer?.Dispose();
        StopRenderTimer();
    }

    [LibraryImport("kernel32")]
    private static partial nint GetModuleHandleW(ushort* lpModuleName);
}