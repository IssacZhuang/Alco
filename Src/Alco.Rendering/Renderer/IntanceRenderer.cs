namespace Alco.Rendering;

/// <summary>
/// High-performance instance renderer for batching and rendering multiple instances of the same type.
/// <br/> Not thread safe but each thread can have its own renderer instance for multi-thread rendering.
/// </summary>
/// <typeparam name="T">The unmanaged type representing instance data.</typeparam>
public unsafe sealed class InstanceRenderer<T> : AutoDisposable, ICommandListener where T : unmanaged
{
    private NativeArrayList<T> _instances = new NativeArrayList<T>();
    private readonly RenderingSystem _renderingSystem;
    private readonly Material _material;
    private readonly List<GraphicsBuffer> _tmpGPUBuffers = new List<GraphicsBuffer>();
    private readonly IRenderContext _renderContext;
    private readonly int _sizePerBuffer;
    private readonly int _maxInstanceCountPerBuffer;
    private GraphicsBuffer? _currentBuffer;
    private int _currentBufferInstanceCount;

    /// <summary>
    /// Initializes a new instance of the InstanceRenderer class.
    /// </summary>
    /// <param name="renderingSystem">The rendering system used for creating graphics resources.</param>
    /// <param name="context">The render context for command submission.</param>
    /// <param name="material">The material to use for rendering instances.</param>
    /// <param name="sizePerBuffer">The size of each GPU buffer in bytes. Default is 256KB.</param>
    internal InstanceRenderer(RenderingSystem renderingSystem, IRenderContext context, Material material, int sizePerBuffer = 256 * 1024)
    {
        _renderingSystem = renderingSystem;
        _material = material;
        _renderContext = context;
        _sizePerBuffer = sizePerBuffer;
        _maxInstanceCountPerBuffer = sizePerBuffer / sizeof(T);
        _currentBufferInstanceCount = 0;

        _renderContext.AddListener(this);
    }

    void ICommandListener.OnCommandBegin()
    {
        // reset state for new frame
        _instances.Clear();
        _currentBufferInstanceCount = 0;
        _currentBuffer = null;

        // return all temporary GPU buffers to pool
        for (int i = 0; i < _tmpGPUBuffers.Count; i++)
        {
            _renderingSystem.GraphicsBufferPool.TryReturnBuffer(_tmpGPUBuffers[i]);
        }
        _tmpGPUBuffers.Clear();
    }

    void ICommandListener.OnCommandEnd()
    {
        // upload remaining instance data
        if (_instances.Length > 0)
        {
            FlushCurrentBuffer();
        }
    }

    /// <summary>
    /// Clears all accumulated instances.
    /// </summary>
    public void Clear()
    {
        _instances.Clear();
        _currentBufferInstanceCount = 0;
    }

    /// <summary>
    /// Adds a single instance to the batch.
    /// </summary>
    /// <param name="instance">The instance data to add.</param>
    public void AddInstance(T instance)
    {
        // check if current buffer has enough capacity
        if (_currentBufferInstanceCount >= _maxInstanceCountPerBuffer)
        {
            FlushCurrentBuffer();
        }

        _instances.Add(instance);
        _currentBufferInstanceCount++;
    }

    /// <summary>
    /// Adds multiple instances to the batch.
    /// </summary>
    /// <param name="instances">The span of instances to add.</param>
    public void AddInstances(ReadOnlySpan<T> instances)
    {
        for (int i = 0; i < instances.Length; i++)
        {
            AddInstance(instances[i]);
        }
    }

    private void FlushCurrentBuffer()
    {
        if (_instances.Length == 0)
            return;

        // get or request new GPU buffer
        if (_currentBuffer == null)
        {
            RequestNewBuffer();
        }

        // upload data to GPU
        uint dataSize = (uint)(_instances.Length * sizeof(T));
        _currentBuffer.UpdateBuffer(_instances.AsReadOnlySpan());

        // add to temporary buffer list for final return
        _tmpGPUBuffers.Add(_currentBuffer);

        // reset state for next batch
        _instances.Clear();
        _currentBufferInstanceCount = 0;
        _currentBuffer = null;
    }

    private void RequestNewBuffer()
    {
        uint bufferSize = (uint)_sizePerBuffer;

        // try to get buffer from pool
        if (_renderingSystem.GraphicsBufferPool.TryGetBuffer(bufferSize, out var buffer))
        {
            _currentBuffer = buffer;
        }
        else
        {
            // create new buffer if pool doesn't have suitable size
            _currentBuffer = _renderingSystem.CreateGraphicsBuffer(bufferSize, $"InstanceRenderer_Buffer_{_tmpGPUBuffers.Count}");
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