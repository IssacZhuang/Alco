using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// The context of the render object. It is a high level encapsulation of the <see cref="GPUCommandBuffer"/>.
/// All APIs in this class are not thread safe, but you can create multiple instances on different threads.
/// </summary>
public sealed class RenderContext : AutoDisposable, IRenderContext
{
    private readonly GPUDevice _device;
    private readonly RenderingSystem _renderingSystem;
    private readonly GPUCommandBuffer _command;
    private GPUCommandBuffer.RenderPass _renderScope;
    private readonly List<ICommandListener> _listeners;
    private GPUFrameBuffer? _framebuffer;

    // Optional: when non-null, End() resolves the given timestamp range into
    // the destination buffer after closing the render pass but before ending
    // the command buffer — the only valid window for wgpu resolve calls.
    private GPUTimestampQuerySet? _pendingResolveQuerySet;
    private GPUBuffer? _pendingResolveDest;
    private uint _pendingResolveFirst;
    private uint _pendingResolveCount;
    private ulong _pendingResolveOffset;

    //cached mesh data
    private Mesh? _mesh;
    private int _subMeshIndex;
    private uint _meshVersion;
    private uint _indexCount;

    /// <summary>
    /// The framebuffer that is currently being rendered to.
    /// </summary>
    public GPUFrameBuffer? Framebuffer => _framebuffer;

    /// <summary>
    /// The command buffer that is currently in use.
    /// </summary>
    public GPUCommandBuffer CommandBuffer
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _command;
    }

    internal RenderContext(RenderingSystem renderingSystem, string name)
    {
        _renderingSystem = renderingSystem;
        _device = renderingSystem.GraphicsDevice;
        _command = _device.CreateCommandBuffer(new CommandBufferDescriptor(name));
        _listeners = new List<ICommandListener>();
    }

    /// <summary>
    /// Adds a command listener to the render context.
    /// </summary>
    /// <param name="listener">The listener to add.</param>
    public void AddListener(ICommandListener listener)
    {
        _listeners.Add(listener);
    }

    /// <summary>
    /// Removes a command listener from the render context.
    /// </summary>
    /// <param name="listener">The listener to remove.</param>
    public void RemoveListener(ICommandListener listener)
    {
        _listeners.Remove(listener);
    }

    /// <summary>
    /// Begin the render context.
    /// </summary>
    /// <param name="target">The framebuffer to render to.</param>
    public void Begin(
        GPUFrameBuffer target,
        ReadOnlySpan<ClearColorData> clearColors,
        float? clearDepth = null,
        uint? clearStencil = null
        )
    {
        _command.Begin();
        _renderScope = _command.BeginRender(target, clearColors, clearDepth, clearStencil);

        _framebuffer = target;

        ClearCache();

        InvokeBegin();
    }

    public void Begin(
        GPUFrameBuffer target,
        float? clearDepth = null,
        uint? clearStencil = null
        )
    {
        Begin(target, ReadOnlySpan<ClearColorData>.Empty, clearDepth, clearStencil);
    }

    public void Begin(
        GPUFrameBuffer target,
        ColorFloat clearColor,
        float? clearDepth = null,
        uint? clearStencil = null
        )
    {
        ReadOnlySpan<ClearColorData> clearColors = stackalloc ClearColorData[1] { new ClearColorData(0, clearColor) };
        Begin(target, clearColors, clearDepth, clearStencil);
    }

    /// <summary>
    /// Begin the render context with GPU timestamp writes at pass begin and end.
    /// Only call this when <see cref="GPUDevice.TimestampQuerySupported"/> is true.
    /// </summary>
    /// <param name="target">The framebuffer to render to.</param>
    /// <param name="clearColors">Attachment clear values.</param>
    /// <param name="querySet">The destination timestamp query set.</param>
    /// <param name="beginQueryIndex">The slot written when the pass begins.</param>
    /// <param name="endQueryIndex">The slot written when the pass ends.</param>
    /// <param name="clearDepth">Optional depth clear value.</param>
    /// <param name="clearStencil">Optional stencil clear value.</param>
    public void Begin(
        GPUFrameBuffer target,
        ReadOnlySpan<ClearColorData> clearColors,
        GPUTimestampQuerySet querySet,
        uint beginQueryIndex,
        uint endQueryIndex,
        float? clearDepth = null,
        uint? clearStencil = null
        )
    {
        _command.Begin();
        _renderScope = _command.BeginRender(target, clearColors, querySet, beginQueryIndex, endQueryIndex, clearDepth, clearStencil);

        _framebuffer = target;

        ClearCache();

        InvokeBegin();
    }

    /// <summary>
    /// Sets the stencil reference value for subsequent draw calls.
    /// </summary>
    /// <param name="value">The stencil reference value.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetStencilReference(uint value)
    {
        _renderScope.SetStencilReference(value);
    }

    /// <summary>
    /// Restricts subsequent draw calls to the specified framebuffer rectangle.
    /// </summary>
    /// <param name="x">The horizontal origin in pixels.</param>
    /// <param name="y">The vertical origin in pixels.</param>
    /// <param name="width">The rectangle width in pixels.</param>
    /// <param name="height">The rectangle height in pixels.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetScissorRect(uint x, uint y, uint width, uint height)
    {
        _renderScope.SetScissorRect(x, y, width, height);
    }

    /// <summary>
    /// Draws a mesh with the specified material.
    /// </summary>
    /// <param name="mesh">The mesh to draw.</param>
    /// <param name="material">The material to use for drawing.</param>
    /// <param name="subMeshIndex">The index of the sub-mesh to draw. Default is 0.</param>
    public void Draw(in Mesh mesh, in Material material, in int subMeshIndex = 0)
    {
        Debug.Assert(_framebuffer != null);
        GraphicsPipelineContext pipelineContext = material.GetPipelineContext(_framebuffer!.AttachmentLayout);
        _renderScope.SetPipeline(pipelineContext.Pipeline!);
        SetMesh(mesh, subMeshIndex);
        material.PushResources(_renderScope);
        _renderScope.DrawIndexed(_indexCount, 1, 0, 0, 0);
    }

    /// <summary>
    /// Draws a mesh with the specified material and push constants.
    /// </summary>
    /// <typeparam name="T">The type of the constant data.</typeparam>
    /// <param name="mesh">The mesh to draw.</param>
    /// <param name="material">The material to use for drawing.</param>
    /// <param name="constant">The constant data to push to the shader.</param>
    /// <param name="subMeshIndex">The index of the sub-mesh to draw. Default is 0.</param>
    /// <exception cref="ArgumentException">Thrown when the size of the constant does not match the push constants size.</exception>
    public unsafe void DrawWithConstant<T>(in Mesh mesh, in Material material, in T constant, in int subMeshIndex = 0) where T : unmanaged
    {
        Debug.Assert(_framebuffer != null);
        GraphicsPipelineContext pipelineContext = material.GetPipelineContext(_framebuffer!.AttachmentLayout);
        // if (pipelineContext.PushConstantsSize != sizeof(T))
        // {
        //     throw new ArgumentException("The size of the constant does not match the push constants size");
        // }
        _renderScope.SetPipeline(pipelineContext.Pipeline!);
        SetMesh(mesh, subMeshIndex);
        material.PushResources(_renderScope);
        PushConstantSafe(constant, pipelineContext.PushConstantsSize);
        _renderScope.DrawIndexed(_indexCount, 1, 0, 0, 0);
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
        Debug.Assert(_framebuffer != null);
        GraphicsPipelineContext pipelineContext = material.GetPipelineContext(_framebuffer!.AttachmentLayout);
        _renderScope.SetPipeline(pipelineContext.Pipeline!);
        SetMesh(mesh, subMeshIndex);
        material.PushResources(_renderScope);
        _renderScope.DrawIndexed(_indexCount, instanceCount, 0, 0, instanceStartIndex);
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
        Debug.Assert(_framebuffer != null);
        GraphicsPipelineContext pipelineContext = material.GetPipelineContext(_framebuffer!.AttachmentLayout);
        _renderScope.SetPipeline(pipelineContext.Pipeline!);
        SetMesh(mesh, subMeshIndex);
        material.PushResources(_renderScope);
        PushConstantSafe(constant, pipelineContext.PushConstantsSize);
        _renderScope.DrawIndexed(_indexCount, instanceCount, 0, 0, instanceStart);
    }

    /// <summary>
    /// Execute the commands recorded in the <see cref="SubRenderContext"/>.
    /// </summary>
    /// <param name="subContext">The sub context to execute.</param>
    public void ExecuteSubContext(SubRenderContext subContext)
    {
        GPURenderBundle renderBundle = subContext.RenderBundle;
        if (!renderBundle.HasBuffer)
        {
            throw new InvalidOperationException("The render bundle of SubRenderContext is not been recorded, try use RenderContext.Begin(GPUAttachmentLayout) to record render commands.");
        }

        _renderScope.ExecuteBundle(renderBundle);
        // the binding of vertex buffer and index buffer will be reset after executing the bundle
        // so we need to clear the cache to rebind the vertex buffer and index buffer
        ClearCache();
    }

    /// <summary>
    /// End the render context.
    /// </summary>
    public void End()
    {
        InvokeEnd();

        _renderScope.Dispose();

        // Resolve timestamps between the render pass close and the command buffer
        // end — the only valid window for wgpu resolve calls.
        if (_pendingResolveQuerySet != null)
        {
            _command.ResolveTimestamps(
                _pendingResolveQuerySet,
                _pendingResolveFirst,
                _pendingResolveCount,
                _pendingResolveDest!,
                _pendingResolveOffset);
            _pendingResolveQuerySet = null;
            _pendingResolveDest = null;
        }

        _command.End();
        _renderingSystem.ScheduleCommandBuffer(_command);
        ClearCache();

        _framebuffer = null;
    }

    /// <summary>
    /// Schedule a timestamp resolve to run after the current render pass closes but
    /// before the command buffer ends. Must be called while a pass is active (after
    /// Begin, before End). The resolve writes the given query range into
    /// <paramref name="destination"/> at <paramref name="destinationOffset"/>.
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
        _pendingResolveQuerySet = querySet;
        _pendingResolveFirst = firstQuery;
        _pendingResolveCount = queryCount;
        _pendingResolveDest = destination;
        _pendingResolveOffset = destinationOffset;
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

        _indexCount = _renderScope.SetMesh(mesh, subMeshIndex);
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
            _renderScope.PushConstants(0, (byte*)ptr, (uint)pushConstantSize);
        }
    }

    /// <summary>
    /// Clears the cached mesh data.
    /// </summary>
    private void ClearCache()
    {
        _mesh = null;
        _subMeshIndex = 0;
    }

    /// <summary>
    /// Invokes the OnCommandBegin event on all listeners.
    /// </summary>
    private void InvokeBegin()
    {
        foreach (var observer in _listeners)
        {
            try
            {
                observer.OnCommandBegin();
            }
            catch (Exception e)
            {
                Log.Error(e);
            }
        }
    }

    /// <summary>
    /// Invokes the OnCommandEnd event on all listeners.
    /// </summary>
    private void InvokeEnd()
    {
        foreach (var observer in _listeners)
        {
            try
            {
                observer.OnCommandEnd();
            }
            catch (Exception e)
            {
                Log.Error(e);
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        _command.Dispose();
    }
}
