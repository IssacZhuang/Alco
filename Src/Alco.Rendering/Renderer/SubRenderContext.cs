using System.Runtime.CompilerServices;
using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// The reusable sub context of the render object. Owns one <see cref="GPURenderBundle"/>
/// and hands out its recording scope through <see cref="BeginPass"/> — all draw commands
/// are recorded inside the scope, consumed with <c>using</c>. The recorded bundle is
/// executed inside a render pass through <see cref="RenderPassScope.ExecuteSubContext"/>.
/// <br/>All APIs in this class are not thread safe, but you can create multiple
/// instances on different threads.
/// </summary>
public sealed class SubRenderContext : AutoDisposable, RenderPassScope.IScopeOwner
{
    private readonly GPURenderBundle _renderBundle;
    private readonly RenderPassScope _passScope;

    private bool _passOpen;

    /// <summary>Whether the bundle holds a recorded command buffer.</summary>
    public bool HasBuffer => _renderBundle.HasBuffer;

    /// <summary>The underlying render bundle executed by a render pass.</summary>
    public GPURenderBundle RenderBundle
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _renderBundle;
    }

    /// <summary>
    /// The recycled recording scope of this sub context. Stable in identity — renderers
    /// bind to it at construction and use it across frames (valid only while recording).
    /// </summary>
    public RenderPassScope Pass
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _passScope;
    }

    internal SubRenderContext(RenderingSystem renderingSystem, string name)
    {
        GPUDevice device = renderingSystem.GraphicsDevice;
        _renderBundle = device.CreateRenderBundle(new RenderBundleDescriptor(name));
        _passScope = new RenderPassScope(this);
    }

    /// <summary>
    /// Begins recording the bundle for the given attachment layout and returns its scope.
    /// Draw commands recorded inside the scope replace the previously recorded bundle
    /// when the scope is disposed.
    /// </summary>
    /// <param name="attachmentLayout">The attachment layout the bundle will be executed with.</param>
    /// <returns>The recording scope, valid until disposed.</returns>
    public RenderPassScope BeginPass(GPUAttachmentLayout attachmentLayout)
    {
        if (_passOpen)
        {
            throw new InvalidOperationException("A recording scope is already open on this sub context; dispose it before beginning a new one.");
        }

        _renderBundle.Begin(attachmentLayout);
        _passOpen = true;
        _passScope.Activate(_renderBundle, attachmentLayout);
        return _passScope;
    }

    void RenderPassScope.IScopeOwner.OnScopeClosed(RenderPassScope scope)
    {
        // A bundle is recorded, not submitted: nothing to do beyond bookkeeping.
        _passOpen = false;
    }

    protected override void Dispose(bool disposing)
    {
        _renderBundle.Dispose();
    }
}
