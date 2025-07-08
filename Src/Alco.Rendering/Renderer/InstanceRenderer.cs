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



    public void Draw(Mesh mesh, int subMeshIndex, ReadOnlySpan<T> instances)
    {
        List<DrawData> draws = PushInstances(instances);
        for (int i = 0; i < draws.Count; i++)
        {
            DrawData draw = draws[i];
            _material.SetBuffer(_shaderId_instanceBuffer, draw.Buffer);
            _renderContext.DrawInstanced(mesh, _material, draw.InstanceCount, draw.InstanceStartIndex, subMeshIndex);
        }
    }

    public void Draw(Mesh mesh, ReadOnlySpan<T> instances)
    {
        Draw(mesh, 0, instances);
    }

    public void DrawWithConstant<TConstant>(Mesh mesh, TConstant constant, int subMeshIndex, ReadOnlySpan<T> instances) where TConstant : unmanaged
    {
        List<DrawData> draws = PushInstances(instances);
        for (int i = 0; i < draws.Count; i++)
        {
            DrawData draw = draws[i];
            _material.SetBuffer(_shaderId_instanceBuffer, draw.Buffer);
            _renderContext.DrawInstancedWithConstant(mesh, _material, draw.InstanceCount, draw.InstanceStartIndex, constant, subMeshIndex);
        }
    }

    public void DrawWithConstant<TConstant>(Mesh mesh, TConstant constant, ReadOnlySpan<T> instances) where TConstant : unmanaged
    {
        DrawWithConstant(mesh, constant, 0, instances);
    }

    /// <summary>
    /// Efficiently batches instances for rendering by minimizing memory copies.
    /// <br/> For large instance sets, complete buffer-sized chunks are sent directly to GPU without intermediate copying.
    /// <br/> Smaller chunks are accumulated in the instance buffer and flushed when full or at frame end.
    /// </summary>
    /// <param name="instances">The span of instances to batch for rendering.</param>
    /// <returns>A list of draw data containing buffer references and instance counts for rendering.</returns>
    private List<DrawData> PushInstances(ReadOnlySpan<T> instances)
    {
        _draws.Clear();

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
            _draws.Add(new DrawData(_currentBuffer, (uint)count, instanceStart));
            return _draws;
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
            _draws.Add(new DrawData(_currentBuffer, (uint)toAdd, instanceStart));
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

        return _draws;
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