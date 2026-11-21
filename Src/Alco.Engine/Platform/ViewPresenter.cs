using System.Runtime.InteropServices;
using Alco.Graphics;
using Alco.Graphics.LowLevel;

namespace Alco.Engine;

/// <summary>
/// Owns the presentation lifecycle of a <see cref="View"/>: swapchain acquisition,
/// ordered context submission, presentation, and resize coordination.
/// </summary>
public sealed class ViewPresenter : AutoDisposable
{
    private readonly View _view;
    private readonly GPUSwapchain? _swapchain;
    private readonly RenderingSystem _renderingSystem;
    private readonly RenderContext _frameContext;
    private readonly List<RenderContext> _frameContexts = new(4);

    private bool _shouldNotifyResize;
    private bool _pendingResize;
    private uint _width;
    private uint _height;

    /// <summary>
    /// Raised in <see cref="EndFrame"/> right before recorded contexts are submitted and presented.
    /// </summary>
    public event Action? OnBeforePresent;

    /// <summary>
    /// Raised in <see cref="EndFrame"/> when the view was resized during the frame.
    /// It is safe to recreate GPU resources sized to the view inside the handler.
    /// </summary>
    public event Action<uint2>? OnResize;

    /// <summary>
    /// Gets the view being presented.
    /// </summary>
    public View View => _view;

    /// <summary>
    /// Gets the swapchain surface format, or <see cref="Format.Undefined"/> when headless.
    /// </summary>
    public Format SurfaceFormat => _swapchain?.Format ?? Format.Undefined;

    /// <summary>
    /// Gets the frame acquired during <see cref="BeginFrame"/>; invalid when <see cref="HasValidFrame"/> is false.
    /// </summary>
    public SwapchainFrame CurrentFrame { get; private set; }

    /// <summary>
    /// Gets whether the presenter holds a drawable swapchain frame for the active update.
    /// </summary>
    public bool HasValidFrame => CurrentFrame.RenderView != null && CurrentFrame.Extent.X > 0 && CurrentFrame.Extent.Y > 0;

    /// <summary>
    /// Gets the presenter-owned context that starts each frame and acquires the swapchain image.
    /// </summary>
    public RenderContext FrameContext => _frameContext;

    /// <summary>
    /// Creates a presenter for the given view targeting <paramref name="renderingSystem"/>.
    /// </summary>
    /// <param name="view">The view whose swapchain is presented.</param>
    /// <param name="renderingSystem">The rendering system used to record and submit frame work.</param>
    public ViewPresenter(View view, RenderingSystem renderingSystem)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(renderingSystem);

        _view = view;
        _swapchain = view.Swapchain;
        _renderingSystem = renderingSystem;
        _frameContext = new RenderContext(renderingSystem);

        _width = math.max(1u, view.Size.X);
        _height = math.max(1u, view.Size.Y);
        _view.OnResize += OnWindowResize;
        _view.OnRestore += OnWindowRestore;
    }

    /// <summary>
    /// Begins frame recording and acquires the swapchain image when the surface is available.
    /// </summary>
    public void BeginFrame()
    {
        CurrentFrame = default;
        _frameContexts.Clear();
        _frameContexts.Add(_frameContext);

        if (_pendingResize && _swapchain != null)
        {
            if (_swapchain.TryResize(new Extent2D(_width, _height), out _))
            {
                _pendingResize = false;
            }
        }

        _frameContext.BeginFrame();

        if (_swapchain == null || _view.WindowMode == WindowMode.Minimized)
        {
            return;
        }

        CurrentFrame = _frameContext.Acquire(_swapchain);
    }

    /// <summary>
    /// Appends an additional active <see cref="RenderContext"/> to be submitted with this frame before presentation.
    /// </summary>
    /// <param name="context">A context from the same rendering system with a begun frame and no open scope.</param>
    public void EnqueueFrameContext(RenderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (ReferenceEquals(context, _frameContext))
        {
            throw new ArgumentException("The presenter's own FrameContext is already included automatically.", nameof(context));
        }

        _frameContexts.Add(context);
    }

    /// <summary>
    /// Submits all frame contexts, presents the acquired swapchain image, and emits deferred resize events.
    /// </summary>
    public void EndFrame()
    {
        try
        {
            OnBeforePresent?.Invoke();

            bool canPresent = _swapchain != null && CurrentFrame.RenderView != null;
            CurrentFrame = default;

            ReadOnlySpan<RenderContext> contexts = CollectionsMarshal.AsSpan(_frameContexts);
            if (canPresent)
            {
                _renderingSystem.SubmitAndPresent(contexts, _swapchain!);
            }
            else if (!contexts.IsEmpty)
            {
                _renderingSystem.Submit(contexts);
            }
        }
        finally
        {
            CurrentFrame = default;
            _frameContexts.Clear();
        }

        if (_shouldNotifyResize)
        {
            _shouldNotifyResize = false;
            OnResize?.Invoke(new uint2(_width, _height));
        }
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _view.OnResize -= OnWindowResize;
            _view.OnRestore -= OnWindowRestore;
            _frameContexts.Clear();
            CurrentFrame = default;
            _frameContext.Destroy();
        }
    }

    private void OnWindowResize(uint2 size)
    {
        if (size.X == 0 || size.Y == 0)
        {
            _ = _swapchain?.TryResize(default, out _);
            return;
        }

        _shouldNotifyResize = true;
        _width = size.X;
        _height = size.Y;

        if (_swapchain == null)
        {
            return;
        }

        _pendingResize = !_swapchain.TryResize(new Extent2D(_width, _height), out _);
    }

    private void OnWindowRestore()
    {
        if (_swapchain == null)
        {
            return;
        }

        uint2 size = _view.Size;
        if (size.X == 0 || size.Y == 0)
        {
            return;
        }

        _width = size.X;
        _height = size.Y;
        _pendingResize = !_swapchain.TryResize(new Extent2D(_width, _height), out _);
    }
}
