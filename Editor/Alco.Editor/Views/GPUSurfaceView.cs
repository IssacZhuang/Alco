using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Threading;
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
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.Rendering.SceneGraph;

namespace Alco.Editor.Views;

public unsafe partial class GPUSurfaceView : NativeControlHost, IDisposable
{
    private class GPURenderOperation : ICustomDrawOperation
    {
        private readonly GPUSurfaceView _view;
        private readonly GPUDevice _device;
        private readonly GPUSwapchain _swapchain;
        private readonly GPUCommandBuffer _commandBuffer;
        public GPURenderOperation(
            GPUSurfaceView view,
            GPUDevice device,
            GPUSwapchain swapchain,
            GPUCommandBuffer commandBuffer
        )
        {
            _view = view;
            _device = device;
            _swapchain = swapchain;
            _commandBuffer = commandBuffer;
        }
        public Avalonia.Rect Bounds => _view.Bounds;

        public void Dispose()
        {
            Log.Info("Dispose");
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
            Log.Info("Begin Draw");
            _commandBuffer.Begin();
            _commandBuffer.SetFrameBuffer(_swapchain.FrameBuffer);
            _commandBuffer.ClearColor(new ColorFloat(0, 1, 1, 1));
            _commandBuffer.End();
            _device.Submit(_commandBuffer);
            _swapchain.Present();
            Log.Success("Clear color completed");
        }

    }

    private GPUDevice? _device;
    private GPURenderOperation? _renderOperation;
    private GPUCommandBuffer? _commandBuffer;
    private GPUSwapchain? _swapchain;


    private readonly DispatcherTimer _renderTimer;
    private int _frameRate = 60;
    public IntPtr Handle { get; private set; }
    public int FrameRate
    {
        get => _frameRate;
        set
        {
            _frameRate = value;
            _renderTimer.Interval = TimeSpan.FromSeconds(1.0f / _frameRate);
        }
    }

    public GPUSurfaceView()
    {
        _frameRate = 60;
        _renderTimer = new DispatcherTimer();
        _renderTimer.Interval = TimeSpan.FromSeconds(1.0f / _frameRate);
        _renderTimer.Tick += (sender, e) =>
        {
            //mark the view as dirty to trigger the render
            InvalidateVisual();
        };
        _renderTimer.Start();
    }

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        GameEngine engine = App.Main.Engine;
        _device = engine.Rendering.GraphicsDevice;


        var handle = base.CreateNativeControlCore(parent);
        Handle = handle.Handle;

        Log.Info($"Native window handle created: {Handle:X}");

        var bounds = Bounds;
        uint width = math.max(1, (uint)bounds.Width);
        uint height = math.max(1, (uint)bounds.Height);
        RenderingSystem renderingSystem = engine.Rendering;
        GPUDevice device = renderingSystem.GraphicsDevice;

        _swapchain = device.CreateSwapchain(
            new SwapchainDescriptor(
                SurfaceSource.CreateWin32Window(Handle, GetModuleHandleW(null)),
                device.PrefferedSurfaceFomat,
                null,
                width,
                height,
                true));

        _commandBuffer = device.CreateCommandBuffer(this.Name ?? "GPUSurfaceView_CommandBuffer");

        _renderOperation = new GPURenderOperation(this, device, _swapchain, _commandBuffer);

        Log.Success($"Swapchain created with size {width}x{height}");

        //StartRenderThread();
        return handle;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (_renderOperation == null) return;
        context.Custom(_renderOperation);
    }


    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        if (_swapchain == null || _device == null) return;
        var bounds = Bounds;
        uint width = math.max(1, (uint)bounds.Width);
        uint height = math.max(1, (uint)bounds.Height);
        _swapchain.Resize(width, height);
        Log.Success($"Resize swapchain to {width}x{height}");
    }



    public void Dispose()
    {
        _swapchain?.Dispose();
        _commandBuffer?.Dispose();
        _renderTimer.Stop();
    }

    [LibraryImport("kernel32")]
    private static partial nint GetModuleHandleW(ushort* lpModuleName);
}