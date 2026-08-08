
using Alco.Graphics;

namespace Alco.Engine;

/// <summary>
/// Engine infrastructure that owns the presentation lifecycle of a <see cref="View"/>:
/// swapchain surface acquisition, present and resize. It holds no render texture and knows
/// nothing about post-processing — a <see cref="ForwardPipeline"/> renders the frame content
/// into <see cref="FrameBuffer"/>, and overlay systems (ImGui, debug stats) draw
/// on top of it afterwards.
/// <br/>The engine drives the presenter explicitly at the frame boundaries:
/// <see cref="BeginFrame"/> before any rendering, <see cref="EndFrame"/> after all drawing.
/// </summary>
public sealed class ViewPresenter : AutoDisposable
{
    private readonly View _view;
    private readonly GPUSwapchain? _swapchain;

    private bool _surfaceAcquired;
    private bool _shouldResize;
    private uint _width;
    private uint _height;

    /// <summary>
    /// Raised in <see cref="EndFrame"/> when the view was resized during the frame.
    /// It is safe to recreate GPU resources sized to the view inside the handler.
    /// </summary>
    public event Action<uint2>? OnResize;

    /// <summary>
    /// The view being presented.
    /// </summary>
    public View View => _view;

    /// <summary>
    /// The swapchain frame buffer of the current frame. Valid between <see cref="BeginFrame"/>
    /// (surface acquired) and <see cref="EndFrame"/> (present); null when the surface is
    /// unavailable (headless view, minimized window or an acquisition failure), in which
    /// case the frame's final output is skipped.
    /// </summary>
    public GPUFrameBuffer? FrameBuffer { get; private set; }

    /// <summary>
    /// The attachment layout of the swapchain frame buffer, for creating GPU resources
    /// compatible with <see cref="FrameBuffer"/> before the first frame begins. Null when
    /// the view has no swapchain (headless view).
    /// </summary>
    public GPUAttachmentLayout? AttachmentLayout => _swapchain?.FrameBuffer.AttachmentLayout;

    /// <summary>
    /// Creates a presenter for the given view.
    /// </summary>
    /// <param name="view">The view whose swapchain is presented.</param>
    public ViewPresenter(View view)
    {
        _view = view;
        _view.OnResize += OnWindowResize;

        _swapchain = view.Swapchain;

        _width = math.max(1, view.Size.X);
        _height = math.max(1, view.Size.Y);
    }

    /// <summary>
    /// Acquires the swapchain surface for the new frame. Called by the engine before any
    /// rendering into <see cref="FrameBuffer"/>.
    /// </summary>
    public void BeginFrame()
    {
        FrameBuffer = null;
        _surfaceAcquired = false;

        if (_swapchain == null)
        {
            return;
        }

        if (!_swapchain.RequestSurfaceTexture())
        {
            return;
        }

        _surfaceAcquired = true;

        if (_view.WindowMode == WindowMode.Minimized)
        {
            return;
        }

        FrameBuffer = _swapchain.FrameBuffer;
    }

    /// <summary>
    /// Presents the frame and processes the deferred view resize. Called by the engine
    /// after all drawing.
    /// </summary>
    public void EndFrame()
    {
        FrameBuffer = null;

        if (_surfaceAcquired)
        {
            _swapchain?.Present();
            _surfaceAcquired = false;
        }

        if (_shouldResize)
        {
            _shouldResize = false;
            OnResize?.Invoke(new uint2(_width, _height));
        }
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _view.OnResize -= OnWindowResize;
        }
    }

    private void OnWindowResize(uint2 size)
    {
        _shouldResize = true;
        _width = size.X;
        _height = size.Y;

        _swapchain?.Resize(_width, _height);
    }
}
