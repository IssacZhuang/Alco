using System.Diagnostics;
using System.Runtime.CompilerServices;
using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// The RAII scope of one open render pass (or render-bundle recording): every draw
/// command lives here. Obtained from <see cref="RenderContext.BeginPass(GPUFrameBuffer, ReadOnlySpan{ClearColorData}, float?, uint?, ReadOnlySpan{AttachmentOps}, AttachmentOps?)"/>
/// or <see cref="SubRenderContext.BeginPass"/> and consumed with <c>using</c>.
/// <br/>The instance is recycled by its owning context: it is valid only between
/// BeginPass and <see cref="Dispose"/> — do not use it past the <c>using</c> block;
/// calls on a closed scope throw <see cref="InvalidOperationException"/>. The identity
/// is stable across frames, so renderers may hold it permanently through
/// <see cref="IRenderContext"/>.
/// <br/>Not thread safe; each owning context has its own scope instance.
/// </summary>
public sealed class RenderPassScope : IRenderContext, IDisposable
{
    /// <summary>Notified by the scope when the pass is closed. Implemented by the
    /// owning contexts to run their after-pass work (timestamp resolve, submission).</summary>
    internal interface IScopeOwner
    {
        /// <summary>Called at the top of <see cref="RenderPassScope.Dispose"/>, while the
        /// native pass/bundle is still open.</summary>
        void OnScopeClosing(RenderPassScope scope);
        /// <summary>Called after the native pass/bundle has been closed.</summary>
        void OnScopeClosed(RenderPassScope scope);
    }

    private readonly IScopeOwner _owner;

    // Backend state: exactly one of direct render pass / render bundle is active.
    private GPUCommandBuffer.RenderPass _pass;
    private GPURenderBundle? _bundle;
    private GPUFrameBuffer? _framebuffer;
    private GPUAttachmentLayout? _attachmentLayout;
    private bool _active;

    // Pending timestamp resolve, recorded after the pass closes (direct recording only).
    private GPUTimestampQuerySet? _pendingResolveQuerySet;
    private GPUBuffer? _pendingResolveDest;
    private uint _pendingResolveFirst;
    private uint _pendingResolveCount;
    private ulong _pendingResolveOffset;

    // Mesh binding cache.
    private Mesh? _mesh;
    private int _subMeshIndex;
    private uint _meshVersion;
    private uint _indexCount;

    private readonly List<ICommandListener> _listeners = new();

    internal RenderPassScope(IScopeOwner owner)
    {
        _owner = owner;
    }

    /// <summary>
    /// The framebuffer the pass is rendering to, or null for a bundle-recording scope.
    /// </summary>
    public GPUFrameBuffer? Framebuffer
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _framebuffer;
    }

    private GPUAttachmentLayout CurrentLayout
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _framebuffer != null ? _framebuffer.AttachmentLayout : _attachmentLayout!;
    }

    internal void Activate(GPUCommandBuffer.RenderPass pass, GPUFrameBuffer target)
    {
        _pass = pass;
        _framebuffer = target;
        _bundle = null;
        _attachmentLayout = null;
        _active = true;
        ClearCache();
    }

    internal void Activate(GPURenderBundle bundle, GPUAttachmentLayout attachmentLayout)
    {
        _bundle = bundle;
        _attachmentLayout = attachmentLayout;
        _framebuffer = null;
        _pass = default;
        _active = true;
        ClearCache();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ThrowIfInactive()
    {
        if (!_active)
        {
            throw new InvalidOperationException("The render pass scope is not recording. Obtain it from RenderContext.BeginPass/SubRenderContext.BeginPass and use it only inside the using block.");
        }
    }

    /// <summary>
    /// Sets the stencil reference value for subsequent draw calls.
    /// Not available while recording a render bundle.
    /// </summary>
    /// <param name="value">The stencil reference value.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetStencilReference(uint value)
    {
        ThrowIfInactive();
        ThrowIfBundle(nameof(SetStencilReference));
        _pass.SetStencilReference(value);
    }

    /// <summary>
    /// Restricts subsequent draw calls to the specified framebuffer rectangle.
    /// Not available while recording a render bundle.
    /// </summary>
    /// <param name="x">The horizontal origin in pixels.</param>
    /// <param name="y">The vertical origin in pixels.</param>
    /// <param name="width">The rectangle width in pixels.</param>
    /// <param name="height">The rectangle height in pixels.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetScissorRect(uint x, uint y, uint width, uint height)
    {
        ThrowIfInactive();
        ThrowIfBundle(nameof(SetScissorRect));
        _pass.SetScissorRect(x, y, width, height);
    }

    /// <summary>
    /// Draws a mesh with the specified material.
    /// </summary>
    /// <param name="mesh">The mesh to draw.</param>
    /// <param name="material">The material to use for drawing.</param>
    /// <param name="subMeshIndex">The index of the sub-mesh to draw. Default is 0.</param>
    public void Draw(in Mesh mesh, in Material material, in int subMeshIndex = 0)
    {
        ThrowIfInactive();
        GraphicsPipelineContext pipelineContext = material.GetPipelineContext(CurrentLayout);
        SetPipeline(pipelineContext.Pipeline!);
        SetMesh(mesh, subMeshIndex);
        PushResources(material);
        DrawIndexed(_indexCount, 1, 0, 0, 0);
    }

    /// <summary>
    /// Draws a mesh with the specified material and push constants.
    /// </summary>
    /// <typeparam name="T">The type of the constant data.</typeparam>
    /// <param name="mesh">The mesh to draw.</param>
    /// <param name="material">The material to use for drawing.</param>
    /// <param name="constant">The constant data to push to the shader.</param>
    /// <param name="subMeshIndex">The index of the sub-mesh to draw. Default is 0.</param>
    public void DrawWithConstant<T>(in Mesh mesh, in Material material, in T constant, in int subMeshIndex = 0) where T : unmanaged
    {
        ThrowIfInactive();
        GraphicsPipelineContext pipelineContext = material.GetPipelineContext(CurrentLayout);
        SetPipeline(pipelineContext.Pipeline!);
        SetMesh(mesh, subMeshIndex);
        PushResources(material);
        PushConstantSafe(constant, pipelineContext.PushConstantsSize);
        DrawIndexed(_indexCount, 1, 0, 0, 0);
    }

    /// <summary>
    /// Draws a mesh multiple times with the specified material.
    /// </summary>
    /// <param name="mesh">The mesh to draw.</param>
    /// <param name="material">The material to use for drawing.</param>
    /// <param name="instanceCount">The number of instances to draw.</param>
    /// <param name="subMeshIndex">The index of the sub-mesh to draw. Default is 0.</param>
    public void DrawInstanced(in Mesh mesh, in Material material, in uint instanceCount, in int subMeshIndex = 0)
    {
        DrawInstanced(mesh, material, instanceCount, 0, subMeshIndex);
    }

    /// <summary>
    /// Draws a mesh multiple times with the specified material.
    /// </summary>
    /// <param name="mesh">The mesh to draw.</param>
    /// <param name="material">The material to use for drawing.</param>
    /// <param name="instanceCount">The number of instances to draw.</param>
    /// <param name="instanceStartIndex">The index of the first instance to draw.</param>
    /// <param name="subMeshIndex">The index of the sub-mesh to draw. Default is 0.</param>
    public void DrawInstanced(in Mesh mesh, in Material material, in uint instanceCount, in uint instanceStartIndex, in int subMeshIndex = 0)
    {
        ThrowIfInactive();
        GraphicsPipelineContext pipelineContext = material.GetPipelineContext(CurrentLayout);
        SetPipeline(pipelineContext.Pipeline!);
        SetMesh(mesh, subMeshIndex);
        PushResources(material);
        DrawIndexed(_indexCount, instanceCount, 0, 0, instanceStartIndex);
    }

    /// <summary>
    /// Draws a mesh multiple times with the specified material and push constants.
    /// </summary>
    /// <typeparam name="T">The type of the constant data.</typeparam>
    /// <param name="mesh">The mesh to draw.</param>
    /// <param name="material">The material to use for drawing.</param>
    /// <param name="instanceCount">The number of instances to draw.</param>
    /// <param name="constant">The constant data to push to the shader.</param>
    /// <param name="subMeshIndex">The index of the sub-mesh to draw. Default is 0.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void DrawInstancedWithConstant<T>(in Mesh mesh, in Material material, in uint instanceCount, in T constant, in int subMeshIndex = 0) where T : unmanaged
    {
        DrawInstancedWithConstant(mesh, material, instanceCount, 0, constant, subMeshIndex);
    }

    /// <summary>
    /// Draws a mesh multiple times with the specified material and push constants, starting from a specific instance.
    /// </summary>
    /// <typeparam name="T">The type of the constant data.</typeparam>
    /// <param name="mesh">The mesh to draw.</param>
    /// <param name="material">The material to use for drawing.</param>
    /// <param name="instanceCount">The number of instances to draw.</param>
    /// <param name="instanceStart">The index of the first instance to draw.</param>
    /// <param name="constant">The constant data to push to the shader.</param>
    /// <param name="subMeshIndex">The index of the sub-mesh to draw. Default is 0.</param>
    public void DrawInstancedWithConstant<T>(in Mesh mesh, in Material material, in uint instanceCount, in uint instanceStart, in T constant, in int subMeshIndex = 0) where T : unmanaged
    {
        ThrowIfInactive();
        GraphicsPipelineContext pipelineContext = material.GetPipelineContext(CurrentLayout);
        SetPipeline(pipelineContext.Pipeline!);
        SetMesh(mesh, subMeshIndex);
        PushResources(material);
        PushConstantSafe(constant, pipelineContext.PushConstantsSize);
        DrawIndexed(_indexCount, instanceCount, 0, 0, instanceStart);
    }

    /// <summary>
    /// Executes the commands recorded in the <see cref="SubRenderContext"/>.
    /// Not available while recording a render bundle (bundles cannot be nested).
    /// </summary>
    /// <param name="subContext">The sub context to execute.</param>
    public void ExecuteSubContext(SubRenderContext subContext)
    {
        ThrowIfInactive();
        ThrowIfBundle(nameof(ExecuteSubContext));
        GPURenderBundle renderBundle = subContext.RenderBundle;
        if (!renderBundle.HasBuffer)
        {
            throw new InvalidOperationException("The render bundle of SubRenderContext is not recorded, record it through SubRenderContext.BeginPass first.");
        }

        _pass.ExecuteBundle(renderBundle);
        // The vertex/index buffer bindings are reset after executing the bundle,
        // so the cache must be cleared to rebind them for subsequent draws.
        ClearCache();
    }

    /// <summary>
    /// Schedules a timestamp resolve to run after the current render pass closes but
    /// before the command buffer ends. Must be called while the pass is open.
    /// Not available while recording a render bundle.
    /// </summary>
    /// <param name="querySet">The source timestamp query set.</param>
    /// <param name="firstQuery">The first source query slot.</param>
    /// <param name="queryCount">The number of slots to resolve.</param>
    /// <param name="destination">A buffer with QueryResolve usage.</param>
    /// <param name="destinationOffset">The byte offset in the destination buffer.</param>
    public void ResolveTimestampsOnEnd(
        GPUTimestampQuerySet querySet,
        uint firstQuery,
        uint queryCount,
        GPUBuffer destination,
        ulong destinationOffset = 0)
    {
        ThrowIfInactive();
        ThrowIfBundle(nameof(ResolveTimestampsOnEnd));
        _pendingResolveQuerySet = querySet;
        _pendingResolveFirst = firstQuery;
        _pendingResolveCount = queryCount;
        _pendingResolveDest = destination;
        _pendingResolveOffset = destinationOffset;
    }

    /// <inheritdoc/>
    public void AddListener(ICommandListener listener)
    {
        _listeners.Add(listener);
    }

    /// <inheritdoc/>
    public void RemoveListener(ICommandListener listener)
    {
        _listeners.Remove(listener);
    }

    /// <summary>
    /// Closes the pass: the owner is notified while the native pass is still open
    /// (<see cref="IScopeOwner.OnScopeClosing"/>), the native pass is closed, and the
    /// owner runs its after-pass work (timestamp resolve, submission of a standalone
    /// context).
    /// </summary>
    public void Dispose()
    {
        ThrowIfInactive();
        _owner.OnScopeClosing(this);
        if (_bundle != null)
        {
            _bundle.End();
        }
        else
        {
            _pass.Dispose();
        }

        _pass = default;
        _active = false;
        _framebuffer = null;
        _attachmentLayout = null;
        ClearCache();
        _owner.OnScopeClosed(this);
    }

    /// <summary>Records the pending timestamp resolve into the command buffer, if any.</summary>
    internal void ResolvePendingTimestamps(GPUCommandBuffer command)
    {
        if (_pendingResolveQuerySet == null)
        {
            return;
        }

        command.ResolveTimestamps(
            _pendingResolveQuerySet,
            _pendingResolveFirst,
            _pendingResolveCount,
            _pendingResolveDest!,
            _pendingResolveOffset);
        _pendingResolveQuerySet = null;
        _pendingResolveDest = null;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void SetPipeline(GPUPipeline pipeline)
    {
        if (_bundle != null)
        {
            _bundle.SetGraphicsPipeline(pipeline);
        }
        else
        {
            _pass.SetPipeline(pipeline);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void PushResources(Material material)
    {
        if (_bundle != null)
        {
            material.PushResources(_bundle);
        }
        else
        {
            material.PushResources(_pass);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void DrawIndexed(uint indexCount, uint instanceCount, uint firstIndex, int vertexOffset, uint firstInstance)
    {
        if (_bundle != null)
        {
            _bundle.DrawIndexed(indexCount, instanceCount, firstIndex, vertexOffset, firstInstance);
        }
        else
        {
            _pass.DrawIndexed(indexCount, instanceCount, firstIndex, vertexOffset, firstInstance);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void SetMesh(in Mesh mesh, in int subMeshIndex)
    {
        if (_mesh == mesh && _subMeshIndex == subMeshIndex && mesh.Version == _meshVersion)
        {
            return;
        }

        _mesh = mesh;
        _subMeshIndex = subMeshIndex;
        _meshVersion = mesh.Version;

        _indexCount = _bundle != null
            ? _bundle.SetMesh(mesh, subMeshIndex)
            : _pass.SetMesh(mesh, subMeshIndex);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private unsafe void PushConstantSafe<T>(in T data, int pushConstantSize) where T : unmanaged
    {
        if (pushConstantSize != sizeof(T))
        {
            pushConstantSize = Math.Min(pushConstantSize, sizeof(T));
        }

        fixed (T* ptr = &data)
        {
            if (_bundle != null)
            {
                _bundle.PushGraphicsConstants(0, (byte*)ptr, (uint)pushConstantSize);
            }
            else
            {
                _pass.PushConstants(0, (byte*)ptr, (uint)pushConstantSize);
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ThrowIfBundle(string operation)
    {
        if (_bundle != null)
        {
            throw new InvalidOperationException($"{operation} is not available while recording a render bundle.");
        }
    }

    private void ClearCache()
    {
        _mesh = null;
        _subMeshIndex = 0;
    }

    /// <summary>Fires <see cref="ICommandListener.OnCommandBegin"/> on all registered
    /// listeners. Driven by the owning context at its command-recording boundary
    /// (buffer open for <see cref="RenderContext"/>, recording begin for
    /// <see cref="SubRenderContext"/>).</summary>
    internal void NotifyListenersBegin()
    {
        for (int i = 0; i < _listeners.Count; i++)
        {
            try
            {
                _listeners[i].OnCommandBegin();
            }
            catch (Exception e)
            {
                Log.Error(e);
            }
        }
    }

    /// <summary>Fires <see cref="ICommandListener.OnCommandEnd"/> on all registered
    /// listeners. Driven by the owning context at its command-recording boundary
    /// (buffer submit/abort for <see cref="RenderContext"/>, still-open bundle for
    /// <see cref="SubRenderContext"/>).</summary>
    internal void NotifyListenersEnd()
    {
        for (int i = 0; i < _listeners.Count; i++)
        {
            try
            {
                _listeners[i].OnCommandEnd();
            }
            catch (Exception e)
            {
                Log.Error(e);
            }
        }
    }
}
