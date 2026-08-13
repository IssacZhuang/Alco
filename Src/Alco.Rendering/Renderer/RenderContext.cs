using System.Numerics;
using System.Runtime.CompilerServices;
using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// The context of the render object. Owns one <see cref="GPUCommandBuffer"/>. Recording
/// requires an active frame scope (<see cref="BeginFrame"/>); all draw commands are
/// recorded inside <see cref="RenderPassScope"/> instances handed out by
/// <see cref="BeginPass(GPUFrameBuffer, ReadOnlySpan{ClearColorData}, float?, uint?, ReadOnlySpan{AttachmentOps}, AttachmentOps?)"/>,
/// consumed with <c>using</c>.
/// <br/>Submission model: unified — the frame scope is the only submitter. Passes (and
/// compute recorded through <see cref="CommandBuffer"/>) never submit; disposing the
/// frame scope submits the whole buffer exactly once. <see cref="RenderGraph"/> drives
/// its shared context through the same public <see cref="BeginFrame"/> path, so graph
/// frames and standalone frames behave identically.
/// <br/>All APIs in this class are not thread safe, but you can create multiple
/// instances on different threads.
/// <br/>Listeners bound through <see cref="RenderPassScope.AddListener"/> fire once per
/// frame scope — when it opens and when it submits or aborts — never per pass.
/// </summary>
public sealed class RenderContext : AutoDisposable, RenderPassScope.IScopeOwner
{
    private readonly RenderingSystem _renderingSystem;
    private readonly GPUCommandBuffer _command;
    private readonly RenderPassScope _passScope;

    private bool _bufferOpen;
    private bool _passOpen;

    /// <summary>
    /// The command buffer that is currently in use. While a frame scope is open,
    /// compute passes may be recorded on it directly; the buffer must never be ended
    /// or submitted by the caller — the frame scope owns submission.
    /// </summary>
    public GPUCommandBuffer CommandBuffer
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _command;
    }

    /// <summary>
    /// The recycled pass scope of this context. Stable in identity — renderers bind to
    /// it at construction and use it across frames (valid only inside an open pass).
    /// </summary>
    public RenderPassScope Pass
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _passScope;
    }

    /// <summary>Whether a pass scope is currently open on this context.</summary>
    public bool IsPassOpen
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _passOpen;
    }

    internal RenderContext(RenderingSystem renderingSystem, string name)
    {
        _renderingSystem = renderingSystem;
        _command = renderingSystem.GraphicsDevice.CreateCommandBuffer(new CommandBufferDescriptor(name));
        _passScope = new RenderPassScope(this);
    }

    /// <summary>
    /// Begins a render pass on the target framebuffer and returns its scope.
    /// Requires an active frame scope (<see cref="BeginFrame"/>); the pass records
    /// into the frame's buffer and never submits — the frame scope submits once
    /// when disposed.
    /// </summary>
    /// <param name="target">The framebuffer to render to.</param>
    /// <param name="clearColors">Attachment clear values.</param>
    /// <param name="clearDepth">Optional depth clear value.</param>
    /// <param name="clearStencil">Optional stencil clear value.</param>
    /// <param name="colorOps">Optional per-color-attachment load/store ops.</param>
    /// <param name="depthOps">Optional depth/stencil load/store ops.</param>
    /// <returns>The pass scope, valid until disposed.</returns>
    public RenderPassScope BeginPass(
        GPUFrameBuffer target,
        ReadOnlySpan<ClearColorData> clearColors,
        float? clearDepth = null,
        uint? clearStencil = null,
        ReadOnlySpan<AttachmentOps> colorOps = default,
        AttachmentOps? depthOps = null)
    {
        ThrowIfPassOpen();
        ThrowIfNoFrame();
        GPUCommandBuffer.RenderPass native = _command.BeginRender(target, clearColors, clearDepth, clearStencil, colorOps, depthOps);
        _passOpen = true;
        _passScope.Activate(native, target);
        return _passScope;
    }

    /// <summary>
    /// Begins a render pass on the target framebuffer without clearing color attachments.
    /// </summary>
    /// <param name="target">The framebuffer to render to.</param>
    /// <param name="clearDepth">Optional depth clear value.</param>
    /// <param name="clearStencil">Optional stencil clear value.</param>
    /// <returns>The pass scope, valid until disposed.</returns>
    public RenderPassScope BeginPass(
        GPUFrameBuffer target,
        float? clearDepth = null,
        uint? clearStencil = null)
    {
        return BeginPass(target, ReadOnlySpan<ClearColorData>.Empty, clearDepth, clearStencil);
    }

    /// <summary>
    /// Begins a render pass on the target framebuffer, clearing its first color attachment.
    /// </summary>
    /// <param name="target">The framebuffer to render to.</param>
    /// <param name="clearColor">The clear color for the first color attachment.</param>
    /// <param name="clearDepth">Optional depth clear value.</param>
    /// <param name="clearStencil">Optional stencil clear value.</param>
    /// <returns>The pass scope, valid until disposed.</returns>
    public RenderPassScope BeginPass(
        GPUFrameBuffer target,
        ColorFloat clearColor,
        float? clearDepth = null,
        uint? clearStencil = null)
    {
        ReadOnlySpan<ClearColorData> clearColors = stackalloc ClearColorData[1] { new ClearColorData(0, clearColor) };
        return BeginPass(target, clearColors, clearDepth, clearStencil);
    }

    /// <summary>
    /// Begins a render pass with GPU timestamp writes at pass begin and end.
    /// Only call this when <see cref="GPUDevice.TimestampQuerySupported"/> is true.
    /// </summary>
    /// <param name="target">The framebuffer to render to.</param>
    /// <param name="clearColors">Attachment clear values.</param>
    /// <param name="querySet">The destination timestamp query set.</param>
    /// <param name="beginQueryIndex">The slot written when the pass begins.</param>
    /// <param name="endQueryIndex">The slot written when the pass ends.</param>
    /// <param name="clearDepth">Optional depth clear value.</param>
    /// <param name="clearStencil">Optional stencil clear value.</param>
    /// <param name="colorOps">Optional per-color-attachment load/store ops.</param>
    /// <param name="depthOps">Optional depth/stencil load/store ops.</param>
    /// <returns>The pass scope, valid until disposed.</returns>
    public RenderPassScope BeginPass(
        GPUFrameBuffer target,
        ReadOnlySpan<ClearColorData> clearColors,
        GPUTimestampQuerySet querySet,
        uint beginQueryIndex,
        uint endQueryIndex,
        float? clearDepth = null,
        uint? clearStencil = null,
        ReadOnlySpan<AttachmentOps> colorOps = default,
        AttachmentOps? depthOps = null)
    {
        ThrowIfPassOpen();
        ThrowIfNoFrame();
        GPUCommandBuffer.RenderPass native = _command.BeginRender(target, clearColors, querySet, beginQueryIndex, endQueryIndex, clearDepth, clearStencil, colorOps, depthOps);
        _passOpen = true;
        _passScope.Activate(native, target);
        return _passScope;
    }

    /// <summary>
    /// Opens the command buffer and returns the frame scope that owns its submission:
    /// passes (and compute recorded via <see cref="CommandBuffer"/>) inside the scope
    /// record into one buffer without submitting; disposing the scope submits once.
    /// This is the only way to open the buffer — <see cref="RenderGraph"/> drives its
    /// shared context through this same path. Throws when a frame is already open
    /// (nested <c>BeginFrame</c>).
    /// </summary>
    /// <returns>The frame scope, to be disposed outermost of any pass scope.</returns>
    public RenderFrameScope BeginFrame()
    {
        Open();
        return new RenderFrameScope(this);
    }

    /// <summary>
    /// Ends a frame opened via <see cref="BeginFrame"/>: submits the buffer, or aborts
    /// it without submitting when a pass scope is still open (a half-open pass cannot be
    /// closed legally mid-dispose — the buffer is discarded so the next frame starts clean).
    /// </summary>
    internal void CloseFrame()
    {
        if (_passOpen)
        {
            Abort();
            return;
        }

        Submit();
    }

    /// <summary>Opens the command buffer for frame recording. Driven by <see cref="BeginFrame"/>.</summary>
    private void Open()
    {
        if (_bufferOpen)
        {
            throw new InvalidOperationException("A frame scope is already open on this context.");
        }

        _command.Begin();
        _bufferOpen = true;
        _passScope.NotifyListenersBegin();
    }

    /// <summary>Ends and submits the open command buffer. No pass scope may be open.
    /// Driven by <see cref="CloseFrame"/>.</summary>
    private void Submit()
    {
        if (!_bufferOpen)
        {
            throw new InvalidOperationException("The command buffer is not open.");
        }
        if (_passOpen)
        {
            throw new InvalidOperationException("A pass scope is still open; dispose it before submitting.");
        }

        _passScope.NotifyListenersEnd();
        _command.End();
        _bufferOpen = false;
        _renderingSystem.ScheduleCommandBuffer(_command);
    }

    void RenderPassScope.IScopeOwner.OnScopeClosing(RenderPassScope scope)
    {
        // Listeners fire per frame scope (open → submit/abort), not per pass,
        // so a frame running many passes notifies exactly once.
    }

    void RenderPassScope.IScopeOwner.OnScopeClosed(RenderPassScope scope)
    {
        scope.ResolvePendingTimestamps(_command);
        _passOpen = false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ThrowIfNoFrame()
    {
        if (!_bufferOpen)
        {
            throw new InvalidOperationException(
                "No frame scope is open on this context. Recording requires one: using var frame = context.BeginFrame().");
        }
    }

    /// <summary>
    /// Force-aborts the open command buffer without submitting it, closing any open
    /// pass scope first. Driven by <see cref="CloseFrame"/> when a frame scope is
    /// disposed with a pass still open.
    /// </summary>
    private void Abort()
    {
        if (_passOpen)
        {
            _passScope.Dispose();
        }
        if (_bufferOpen)
        {
            _passScope.NotifyListenersEnd();
            _command.End();
            _bufferOpen = false;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ThrowIfPassOpen()
    {
        if (_passOpen)
        {
            throw new InvalidOperationException("A pass scope is already open on this context; dispose it before beginning a new one.");
        }
    }

    protected override void Dispose(bool disposing)
    {
        _command.Dispose();
    }
}
