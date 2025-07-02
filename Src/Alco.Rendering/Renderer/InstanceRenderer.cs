namespace Alco.Rendering;

/// <summary>
/// High-performance instance renderer for batching and rendering multiple instances of the same type.
/// <br/> Not thread safe but each thread can have its own renderer instance for multi-thread rendering.
/// </summary>
/// <typeparam name="T">The unmanaged type representing instance data.</typeparam>
public unsafe sealed class InstanceRenderer<T> : AutoDisposable, ICommandListener where T : unmanaged
{
    private NativeBuffer<T> _instances = new NativeBuffer<T>();
    private int _instanceCount = 0;
    private readonly RenderingSystem _renderingSystem;
    private readonly List<GraphicsBuffer> _tmpGPUBuffers = new List<GraphicsBuffer>();
    private readonly List<uint> _drawCounts = new List<uint>();
    private readonly IRenderContext _renderContext;
    private readonly Material _material;
    private readonly int _sizePerBuffer;
    private readonly int _maxInstanceCountPerBuffer;
    private readonly uint _shaderId_instanceBuffer;

    private int _bufferIndex = 0;

    public string Name { get; }

    private ReadOnlySpan<T> CurrentInstances => new ReadOnlySpan<T>(_instances.UnsafePointer, _instanceCount);

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
        _instances.EnsureSizeWithoutCopy(_maxInstanceCountPerBuffer);

        // Get shader resource ID for instance buffer
        _shaderId_instanceBuffer = _material.GetResourceId(instanceBufferShaderId);

        _renderContext.AddListener(this);
    }

    void ICommandListener.OnCommandBegin()
    {
        // reset state for new frame
        _instanceCount = 0;
        _bufferIndex = 0;
        _drawCounts.Clear();
    }

    void ICommandListener.OnCommandEnd()
    {
        // upload remaining instance data
        if (_instanceCount > 0)
        {
            FlushCurrentBuffer(CurrentInstances);
        }
    }

    /// <summary>
    /// Clears all accumulated instances.
    /// </summary>
    public void Clear()
    {
        _instanceCount = 0;
        _drawCounts.Clear();
        _bufferIndex = 0;
    }

    /// <summary>
    /// Adds a single instance to the batch.
    /// </summary>
    /// <param name="instance">The instance data to add.</param>
    public void AddInstance(T instance)
    {
        // check if current buffer has enough capacity
        if (_instanceCount >= _maxInstanceCountPerBuffer)
        {
            FlushCurrentBuffer(CurrentInstances);
        }

        _instances.UnsafePointer[_instanceCount++] = instance;
    }

    /// <summary>
    /// Adds multiple instances to the batch. For large batches (>= buffer capacity), 
    /// instances are uploaded directly to GPU to avoid unnecessary copying to intermediate buffer.
    /// </summary>
    /// <param name="instances">The span of instances to add.</param>
    public void AddInstances(ReadOnlySpan<T> instances)
    {
        if (instances.Length == 0)
            return;

        int remainingInstances = instances.Length;
        int currentOffset = 0;

        // If we have existing instances and the new batch would exceed the buffer capacity,
        // flush the current buffer first
        if (_instanceCount > 0 && _instanceCount + instances.Length > _maxInstanceCountPerBuffer)
        {
            FlushCurrentBuffer(CurrentInstances);
        }

        // Process large batches directly to GPU to avoid copying to intermediate buffer
        while (remainingInstances >= _maxInstanceCountPerBuffer)
        {
            var batch = instances.Slice(currentOffset, _maxInstanceCountPerBuffer);
            FlushCurrentBuffer(batch);

            currentOffset += _maxInstanceCountPerBuffer;
            remainingInstances -= _maxInstanceCountPerBuffer;
        }

        // Add remaining instances to the buffer
        if (remainingInstances > 0)
        {
            var remaining = instances.Slice(currentOffset, remainingInstances);

            // Check if remaining instances would exceed current buffer capacity
            if (_instanceCount + remainingInstances > _maxInstanceCountPerBuffer)
            {
                FlushCurrentBuffer(CurrentInstances);
            }

            // Copy remaining instances to buffer
            var destination = new Span<T>(_instances.UnsafePointer + _instanceCount, remainingInstances);
            remaining.CopyTo(destination);
            _instanceCount += remainingInstances;
        }
    }

    /// <summary>
    /// Draws all accumulated instances using the bound material.
    /// </summary>
    /// <param name="mesh">The mesh to draw.</param>
    /// <param name="subMeshIndex">The index of the sub-mesh to draw. Default is 0.</param>
    public void Draw(Mesh mesh, int subMeshIndex = 0)
    {
        for (int i = 0; i < _drawCounts.Count; i++)
        {
            // Bind the corresponding instance buffer for this draw call
            _material.SetBuffer(_shaderId_instanceBuffer, _tmpGPUBuffers[i]);
            _renderContext.DrawInstanced(mesh, _material, _drawCounts[i], subMeshIndex);
        }
    }

    /// <summary>
    /// Draws all accumulated instances using the bound material with push constants.
    /// </summary>
    /// <typeparam name="TConstant">The type of the constant data.</typeparam>
    /// <param name="mesh">The mesh to draw.</param>
    /// <param name="constant">The constant data to push to the shader.</param>
    /// <param name="subMeshIndex">The index of the sub-mesh to draw. Default is 0.</param>
    public void DrawWithConstant<TConstant>(Mesh mesh, TConstant constant, int subMeshIndex = 0) where TConstant : unmanaged
    {
        for (int i = 0; i < _drawCounts.Count; i++)
        {
            // Bind the corresponding instance buffer for this draw call
            _material.SetBuffer(_shaderId_instanceBuffer, _tmpGPUBuffers[i]);
            _renderContext.DrawInstancedWithConstant(mesh, _material, _drawCounts[i], constant, subMeshIndex);
        }
    }

    private void FlushCurrentBuffer(ReadOnlySpan<T> instances)
    {
        if (instances.Length == 0)
            return;

        // get or request new GPU buffer
        GraphicsBuffer buffer = RequestNewBuffer();

        // upload data to GPU
        buffer.UpdateBuffer(instances);

        _drawCounts.Add((uint)instances.Length);

        // reset state for next batch
        _instanceCount = 0;
    }

    private GraphicsBuffer RequestNewBuffer()
    {
        uint bufferSize = (uint)_sizePerBuffer;

        if (_bufferIndex < _tmpGPUBuffers.Count)
        {
            return _tmpGPUBuffers[_bufferIndex++];
        }
        else if (_renderingSystem.GraphicsBufferPool.TryGetBuffer(bufferSize, out var buffer))
        {
            _tmpGPUBuffers.Add(buffer);
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
        _renderContext.RemoveListener(this);
        // dispose native resources
        _instances.Dispose();
        // return all temporary GPU buffers to pool
        for (int i = 0; i < _tmpGPUBuffers.Count; i++)
        {
            _renderingSystem.GraphicsBufferPool.TryReturnBuffer(_tmpGPUBuffers[i]);
        }
        _tmpGPUBuffers.Clear();


    }
}