using System.Runtime.CompilerServices;

namespace Alco.Rendering;

/// <summary>
/// High-performance instance renderer for batching and rendering multiple instances of the same type.
/// <br/> Not thread safe but each thread can have its own renderer instance for multi-thread rendering.
/// </summary>
/// <typeparam name="T">The unmanaged type representing instance data.</typeparam>
public unsafe sealed class InstanceRenderer<T> : AutoDisposable, ICommandListener where T : unmanaged
{
    private struct DrawData
    {
        public GraphicsBuffer Buffer;
        public uint InstanceCount;
        public uint InstanceStartIndex;

        public DrawData(GraphicsBuffer buffer, uint instanceCount, uint instanceStartIndex)
        {
            Buffer = buffer;
            InstanceCount = instanceCount;
            InstanceStartIndex = instanceStartIndex;
        }
    }

    private readonly RenderingSystem _renderingSystem;
    private readonly List<GraphicsBuffer> _tmpGPUBuffers = new List<GraphicsBuffer>();
    private readonly List<DrawData> _draws = new List<DrawData>();
    private readonly IRenderContext _renderContext;
    private readonly Material _material;
    private readonly int _sizePerBuffer;
    private readonly int _maxInstanceCountPerBuffer;
    private readonly uint _shaderId_instanceBuffer;

    private GraphicsBuffer? _currentBuffer;
    private int _bufferIndex = 0;

    private readonly ArrayBuffer<T> _instances = new ArrayBuffer<T>();
    private int _instanceCount = 0;

    public string Name { get; }

    private ReadOnlySpan<T> CurrentInstances => _instances.AsSpan(0, _instanceCount);

    /// <summary>
    /// Initializes a new instance of the InstanceRenderer class.
    /// </summary>
    /// <param name="renderingSystem">The rendering system used for creating graphics resources.</param>
    /// <param name="context">The render context for command submission.</param>
    /// <param name="material">The material to use for rendering instances.</param>
    /// <param name="instanceBufferShaderId">The shader resource name for the instance buffer. Default is "_instances".</param>
    /// <param name="sizePerBuffer">The size of each GPU buffer in bytes. Default is 256KB.</param>
    /// <param name="name">The name of the renderer. Default is "unnamed_instance_renderer".</param>
    internal InstanceRenderer(
        RenderingSystem renderingSystem,
        IRenderContext context,
        Material material,
        string instanceBufferShaderId = ShaderResourceId.Instances,
        int sizePerBuffer = 256 * 1024,
        string name = "unnamed_instance_renderer")
    {
        Name = name;

        _renderingSystem = renderingSystem;
        _renderContext = context;
        _material = material.CreateInstance();
        _sizePerBuffer = sizePerBuffer;
        _maxInstanceCountPerBuffer = sizePerBuffer / sizeof(T);

        // Get shader resource ID for instance buffer
        _shaderId_instanceBuffer = _material.GetResourceId(instanceBufferShaderId);

        _renderContext.AddListener(this);
    }

    void ICommandListener.OnCommandBegin()
    {
        // reset state for new frame
        Clear();
    }

    void ICommandListener.OnCommandEnd()
    {
        if (_currentBuffer != null && _instanceCount > 0)
        {
            ReadOnlySpan<T> span = _instances.AsSpan(0, _instanceCount);
            _currentBuffer.UpdateBuffer(span);
        }
    }

    /// <summary>
    /// Clears all accumulated instances.
    /// </summary>
    public void Clear()
    {
        _instanceCount = 0;
        _draws.Clear();
        _bufferIndex = 0;
        _currentBuffer = null;
    }



    /// <summary>
    /// Draws all instances in the queue for the specified mesh and submesh, then clears the queue.
    /// This operation enqueues the provided instances, renders all queued instances, and clears the internal draw queue.
    /// </summary>
    /// <param name="mesh">The mesh to render.</param>
    /// <param name="subMeshIndex">The index of the submesh to render.</param>
    /// <param name="instances">The instances to add to the queue before rendering.</param>
    public void Draw(Mesh mesh, int subMeshIndex, ReadOnlySpan<T> instances)
    {
        EnqueueInstances(instances);
        for (int i = 0; i < _draws.Count; i++)
        {
            DrawData draw = _draws[i];
            _material.SetBuffer(_shaderId_instanceBuffer, draw.Buffer);
            _renderContext.DrawInstanced(mesh, _material, draw.InstanceCount, draw.InstanceStartIndex, subMeshIndex);
        }
        ClearDraws();
    }

    /// <summary>
    /// Draws all instances in the queue for the specified mesh (using submesh index 0), then clears the queue.
    /// This operation enqueues the provided instances, renders all queued instances, and clears the internal draw queue.
    /// </summary>
    /// <param name="mesh">The mesh to render.</param>
    /// <param name="instances">The instances to add to the queue before rendering.</param>
    public void Draw(Mesh mesh, ReadOnlySpan<T> instances)
    {
        Draw(mesh, 0, instances);
    }

    /// <summary>
    /// Draws all instances currently in the queue for the specified mesh and submesh, then clears the queue.
    /// This operation renders all queued instances without adding new ones, and clears the internal draw queue.
    /// </summary>
    /// <param name="mesh">The mesh to render.</param>
    /// <param name="subMeshIndex">The index of the submesh to render.</param>
    public void Draw(Mesh mesh, int subMeshIndex)
    {
        Draw(mesh, subMeshIndex, ReadOnlySpan<T>.Empty);
    }

    /// <summary>
    /// Draws all instances currently in the queue for the specified mesh (using submesh index 0), then clears the queue.
    /// This operation renders all queued instances without adding new ones, and clears the internal draw queue.
    /// </summary>
    /// <param name="mesh">The mesh to render.</param>
    public void Draw(Mesh mesh)
    {
        Draw(mesh, 0, ReadOnlySpan<T>.Empty);
    }

    /// <summary>
    /// Draws all instances in the queue for the specified mesh and submesh with a constant value, then clears the queue.
    /// This operation enqueues the provided instances, renders all queued instances with the constant value, and clears the internal draw queue.
    /// </summary>
    /// <typeparam name="TConstant">The unmanaged type of the constant value.</typeparam>
    /// <param name="mesh">The mesh to render.</param>
    /// <param name="constant">The constant value to pass to the shader.</param>
    /// <param name="subMeshIndex">The index of the submesh to render.</param>
    /// <param name="instances">The instances to add to the queue before rendering.</param>
    public void DrawWithConstant<TConstant>(Mesh mesh, TConstant constant, int subMeshIndex, ReadOnlySpan<T> instances) where TConstant : unmanaged
    {
        EnqueueInstances(instances);
        for (int i = 0; i < _draws.Count; i++)
        {
            DrawData draw = _draws[i];
            _material.SetBuffer(_shaderId_instanceBuffer, draw.Buffer);
            _renderContext.DrawInstancedWithConstant(mesh, _material, draw.InstanceCount, draw.InstanceStartIndex, constant, subMeshIndex);
        }
        ClearDraws();
    }

    /// <summary>
    /// Draws all instances in the queue for the specified mesh (using submesh index 0) with a constant value, then clears the queue.
    /// This operation enqueues the provided instances, renders all queued instances with the constant value, and clears the internal draw queue.
    /// </summary>
    /// <typeparam name="TConstant">The unmanaged type of the constant value.</typeparam>
    /// <param name="mesh">The mesh to render.</param>
    /// <param name="constant">The constant value to pass to the shader.</param>
    /// <param name="instances">The instances to add to the queue before rendering.</param>
    public void DrawWithConstant<TConstant>(Mesh mesh, TConstant constant, ReadOnlySpan<T> instances) where TConstant : unmanaged
    {
        DrawWithConstant(mesh, constant, 0, instances);
    }

    /// <summary>
    /// Draws all instances currently in the queue for the specified mesh and submesh with a constant value, then clears the queue.
    /// This operation renders all queued instances with the constant value without adding new ones, and clears the internal draw queue.
    /// </summary>
    /// <typeparam name="TConstant">The unmanaged type of the constant value.</typeparam>
    /// <param name="mesh">The mesh to render.</param>
    /// <param name="constant">The constant value to pass to the shader.</param>
    /// <param name="subMeshIndex">The index of the submesh to render.</param>
    public void DrawWithConstant<TConstant>(Mesh mesh, TConstant constant, int subMeshIndex) where TConstant : unmanaged
    {
        DrawWithConstant(mesh, constant, subMeshIndex, ReadOnlySpan<T>.Empty);
    }

    /// <summary>
    /// Draws all instances currently in the queue for the specified mesh (using submesh index 0) with a constant value, then clears the queue.
    /// This operation renders all queued instances with the constant value without adding new ones, and clears the internal draw queue.
    /// </summary>
    /// <typeparam name="TConstant">The unmanaged type of the constant value.</typeparam>
    /// <param name="mesh">The mesh to render.</param>
    /// <param name="constant">The constant value to pass to the shader.</param>
    public void DrawWithConstant<TConstant>(Mesh mesh, TConstant constant) where TConstant : unmanaged
    {
        DrawWithConstant(mesh, constant, 0, ReadOnlySpan<T>.Empty);
    }

    /// <summary>
    /// Enqueues instances to be rendered on the next call to <see cref="Draw(Mesh, ReadOnlySpan{T})"/> or <see cref="DrawWithConstant{TConstant}(Mesh, TConstant, ReadOnlySpan{T})"/>.
    /// </summary>
    /// <param name="instances">The span of instances to enqueue for rendering.</param>
    public void EnqueueInstances(ReadOnlySpan<T> instances)
    {
        if (instances.IsEmpty)
        {
            return;
        }

        // Implementation efficiently batches instances for rendering by minimizing memory copies.
        // For large instance sets, complete buffer-sized chunks are sent directly to GPU without intermediate copying.
        // Smaller chunks are accumulated in the instance buffer and flushed when full or at frame end.
        // If the last drawData is not full, merges instances into it when possible.

        _currentBuffer ??= RequestNewBuffer();

        int count = instances.Length;
        int remainInBuffer = _maxInstanceCountPerBuffer - _instanceCount;

        _instances.SetSizeWithoutCopy(Math.Max(_instanceCount + count, _maxInstanceCountPerBuffer));

        if (count < remainInBuffer)
        {
            uint instanceStart = (uint)_instanceCount;
            Span<T> span = _instances.AsSpan(_instanceCount, count);
            instances.CopyTo(span);
            _instanceCount += count;

            // Try to merge with last DrawData, or add new one if merge fails
            if (!TryMergeToLastDrawData(_currentBuffer, (uint)count, instanceStart))
            {
                _draws.Add(new DrawData(_currentBuffer, (uint)count, instanceStart));
            }
            return;
        }

        // Handle case where instances don't fit in current buffer
        int offset = 0;

        // First, fill the current buffer if it has space

        if (remainInBuffer > 0)
        {
            int toAdd = Math.Min(count, remainInBuffer);
            uint instanceStart = (uint)_instanceCount;
            Span<T> span = _instances.AsSpan(_instanceCount, toAdd);
            instances.Slice(0, toAdd).CopyTo(span);
            _instanceCount += toAdd;

            // Try to merge with last DrawData, or add new one if merge fails
            if (!TryMergeToLastDrawData(_currentBuffer, (uint)toAdd, instanceStart))
            {
                _draws.Add(new DrawData(_currentBuffer, (uint)toAdd, instanceStart));
            }

            offset += toAdd;

            // If buffer is full, immediately update to GPU and get new buffer

            if (_instanceCount >= _maxInstanceCountPerBuffer)
            {
                ReadOnlySpan<T> bufferSpan = _instances.AsSpan(0, _instanceCount);
                _currentBuffer.UpdateBuffer(bufferSpan);
                _currentBuffer = RequestNewBuffer();
            }
        }

        // Process complete buffer-sized chunks directly without copying to _instances

        while (offset + _maxInstanceCountPerBuffer <= count)
        {
            // Directly update a full buffer worth of data to GPU
            ReadOnlySpan<T> directSpan = instances.Slice(offset, _maxInstanceCountPerBuffer);
            _currentBuffer.UpdateBuffer(directSpan);
            _draws.Add(new DrawData(_currentBuffer, (uint)_maxInstanceCountPerBuffer, 0));
            offset += _maxInstanceCountPerBuffer;

            // Always get new buffer after direct update
            _currentBuffer = RequestNewBuffer();
        }

        // Handle remaining instances (less than a full buffer)

        if (offset < count)
        {
            int remaining = count - offset;
            Span<T> span = _instances.AsSpan(0, remaining);
            instances.Slice(offset, remaining).CopyTo(span);
            _instanceCount = remaining;
            _draws.Add(new DrawData(_currentBuffer, (uint)remaining, 0));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ClearDraws()
    {
        _draws.Clear();
    }

    /// <summary>
    /// Attempts to merge the specified draw data with the last DrawData in the list.
    /// </summary>
    /// <param name="buffer">The graphics buffer for the new draw data.</param>
    /// <param name="instanceCount">The number of instances in the new draw data.</param>
    /// <param name="instanceStartIndex">The starting index of instances in the new draw data.</param>
    /// <returns>True if successfully merged; otherwise, false.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryMergeToLastDrawData(GraphicsBuffer buffer, uint instanceCount, uint instanceStartIndex)
    {
        if (_draws.Count == 0)
            return false;

        int lastIndex = _draws.Count - 1;
        DrawData lastDraw = _draws[lastIndex];

        // Can merge if same buffer and contiguous instances
        if (lastDraw.Buffer == buffer &&
            lastDraw.InstanceStartIndex + lastDraw.InstanceCount == instanceStartIndex)
        {
            // Merge by extending the last draw's instance count
            lastDraw.InstanceCount += instanceCount;
            _draws[lastIndex] = lastDraw;
            return true;
        }

        return false;
    }


    private GraphicsBuffer RequestNewBuffer()
    {
        uint bufferSize = (uint)_sizePerBuffer;

        if (_bufferIndex < _tmpGPUBuffers.Count)
        {
            _instanceCount = 0;
            return _tmpGPUBuffers[_bufferIndex++];
        }
        else if (_renderingSystem.GraphicsBufferPool.TryGetBuffer(bufferSize, out var buffer))
        {
            _tmpGPUBuffers.Add(buffer);
            _instanceCount = 0;
            _bufferIndex++;
            return buffer;
        }
        else
        {
            //should not happen
            throw new Exception("No buffer available in pool");
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // return all temporary GPU buffers to pool
            for (int i = 0; i < _tmpGPUBuffers.Count; i++)
            {
                _renderingSystem.GraphicsBufferPool.TryReturnBuffer(_tmpGPUBuffers[i]);
            }
        }

        _renderContext.RemoveListener(this);
        _tmpGPUBuffers.Clear();


    }
}