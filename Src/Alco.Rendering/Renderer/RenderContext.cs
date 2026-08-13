using System.Numerics;
using System.Runtime.CompilerServices;
using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// The context of the render object. Owns one <see cref="GPUCommandBuffer"/> and
/// hands out <see cref="RenderPassScope"/> instances through <see cref="BeginPass(GPUFrameBuffer, ReadOnlySpan{ClearColorData}, float?, uint?, ReadOnlySpan{AttachmentOps}, AttachmentOps?)"/>
/// — all draw commands are recorded inside the scope, consumed with <c>using</c>.
/// <br/>Submission model: when the context itself opened the command buffer (a
/// standalone context), closing the scope submits immediately — byte-for-byte the old
/// Begin/End behavior. When the buffer was opened externally (<see cref="Open"/>,
/// e.g. by a <see cref="RenderGraph"/>), passes record into the shared buffer and
/// nothing submits until <see cref="Submit"/> is called — one submission per frame.
/// <br/>All APIs in this class are not thread safe, but you can create multiple
/// instances on different threads.
/// <br/>Listeners bound through <see cref="RenderPassScope.AddListener"/> fire once per
/// command-buffer cycle — when the buffer opens (<see cref="Open"/>/auto-open) and when it
/// submits or aborts — not per pass, so listeners on a shared (graph) context see exactly
/// one begin/end pair per frame no matter how many passes the frame contains.
/// </summary>
public sealed class RenderContext : AutoDisposable, RenderPassScope.IScopeOwner
{
    private readonly RenderingSystem _renderingSystem;
    private readonly GPUCommandBuffer _command;
    private readonly RenderPassScope _passScope;

    private bool _bufferOpen;
    private bool _autoOpenedBuffer;
    private bool _passOpen;

    /// <summary>
    /// The command buffer that is currently in use. In shared mode (buffer opened via
    /// <see cref="Open"/>) compute passes may be recorded on it directly; the buffer
    /// must never be ended or submitted by the caller.
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

    /// <summary>Whether the command buffer is open (opened by <see cref="Open"/> or
    /// auto-opened by an active pass).</summary>
    internal bool IsBufferOpen
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _bufferOpen;
    }

    internal RenderContext(RenderingSystem renderingSystem, string name)
    {
        _renderingSystem = renderingSystem;
        _command = renderingSystem.GraphicsDevice.CreateCommandBuffer(new CommandBufferDescriptor(name));
        _passScope = new RenderPassScope(this);
    }

    /// <summary>
    /// Begins a render pass on the target framebuffer and returns its scope.
    /// A standalone context auto-opens its command buffer and submits when the scope
    /// is disposed; on a shared context (buffer opened via <see cref="Open"/>) the
    /// pass is recorded into the shared buffer without submitting.
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
        EnsureBufferOpen();
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
        EnsureBufferOpen();
        GPUCommandBuffer.RenderPass native = _command.BeginRender(target, clearColors, querySet, beginQueryIndex, endQueryIndex, clearDepth, clearStencil, colorOps, depthOps);
        _passOpen = true;
        _passScope.Activate(native, target);
        return _passScope;
    }

    /// <summary>
    /// Opens the command buffer for external (shared) recording. While the buffer is
    /// open, passes record into it without submitting; call <see cref="Submit"/> to
    /// end and submit. Engine-internal: driven by <see cref="RenderGraph"/>.
    /// </summary>
    internal void Open()
    {
        if (_bufferOpen)
        {
            throw new InvalidOperationException("The command buffer is already open.");
        }

        _command.Begin();
        _bufferOpen = true;
        _passScope.NotifyListenersBegin();
    }

    /// <summary>
    /// Ends the command buffer opened via <see cref="Open"/> and submits it. No pass
    /// scope may be open. Engine-internal: driven by <see cref="RenderGraph"/>.
    /// </summary>
    internal void Submit()
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
        // Listeners fire per command-buffer cycle (open → submit), not per pass,
        // so a shared context running many passes per frame notifies once per frame.
    }

    void RenderPassScope.IScopeOwner.OnScopeClosed(RenderPassScope scope)
    {
        scope.ResolvePendingTimestamps(_command);
        _passOpen = false;
        if (_autoOpenedBuffer)
        {
            _autoOpenedBuffer = false;
            Submit();
        }
    }

    private void EnsureBufferOpen()
    {
        if (_bufferOpen)
        {
            return;
        }

        _command.Begin();
        _bufferOpen = true;
        _autoOpenedBuffer = true;
        _passScope.NotifyListenersBegin();
    }

    /// <summary>
    /// Force-aborts the open command buffer without submitting it, closing any open
    /// pass scope first. Error recovery only — used by <see cref="RenderGraph"/> when
    /// a node threw mid-pass.
    /// </summary>
    internal void Abort()
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
            _autoOpenedBuffer = false;
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
