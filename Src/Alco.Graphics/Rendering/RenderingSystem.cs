// Example-level rendering owner: shared buffer/texture storage, bindless descriptor heaps, and wrapper retirement behind submissions.

using System.Diagnostics;
using Alco.Graphics.LowLevel;

namespace Alco.Graphics;

/// <summary>Policy applied when a material draw encounters an uncached graphics or mesh pipeline.</summary>
public enum PipelineMissPolicy : byte
{
    /// <summary>Compile and cache the pipeline automatically on demand.</summary>
    Create,

    /// <summary>Compile and cache the pipeline on demand and report a nonfatal diagnostic.</summary>
    CreateAndWarn,

    /// <summary>Throw an <see cref="InvalidOperationException"/> without compiling a pipeline.</summary>
    Throw,
}

/// <summary>Owns shared GPU storage and descriptor heaps. Independent resources may be created and reclaimed concurrently.</summary>
/// <remarks>Initialize and destroy with exclusive access; keep the device alive until destruction.</remarks>
public sealed class RenderingSystem
{
    private const ulong DefaultUploadStagingByteCount = 4 * 1024 * 1024;

    private readonly GPUDevice _device;
    private readonly GPUHeapPool _bufferPool;
    private readonly TextureHeapPool _texturePool;
    private readonly GPUTextureDescriptorHeap _textureDescriptors;
    private readonly GPUSamplerDescriptorHeap _samplerDescriptors;
    private readonly DescriptorSlotAllocator _textureSlots;
    private readonly DescriptorSlotAllocator _samplerSlots;
    private readonly Texture _textureWhite;
    private readonly SharedSamplers _samplers;
    private readonly UploadQueue _uploadQueue;
    private readonly TimelinePoint _uploadTimeline;
    private readonly GPUTimelineSemaphore _frameTimeline;
    private GPUCommandBuffer[] _submitCommands = new GPUCommandBuffer[1];
    private ulong _frameValue;
    private readonly Lock _bufferLock = new();
    private readonly Lock _textureLock = new();
    private readonly Lock _samplerLock = new();
    private readonly Lock _releaseLock = new();
    private readonly Lock _uploadLock = new();
    private readonly Lock _queueZeroSubmitLock = new();
    private readonly Lock _shaderLock = new();
    private ShaderSystem? _shaders;
    private readonly List<PendingRelease> _pendingReleases = [];
    private readonly List<Submission> _submissions = [];
    private readonly List<Texture.InitializationState> _initializations = [];
    private readonly List<PendingTextureRelease> _textureReleases = [];
    private List<PendingRelease>? _postedReleases;
    private ulong _submissionValue;
    private int _ticking;
    private uint _liveBuffers;
    private uint _liveTextures;
    private uint _liveSamplers;
    private byte _pipelineMissPolicy;
    private readonly object _warmupSync = new();
    private readonly Dictionary<WarmUpKey, Task> _pendingWarmups = [];
    private bool _warmupClosed;
    private bool _destroyed;

    /// <summary>Creates growable buffer and texture storage without a total byte budget, and the device's standard bindless descriptor heaps.</summary>
    /// <param name="device">The device; it must outlive this system.</param>
    /// <param name="textureHeapByteCount">The initial texture heap size in bytes; storage grows without moving live textures.</param>
    /// <param name="bufferHeapByteCount">The initial buffer heap size, a positive multiple of 16 bytes; live buffers never move.</param>
    /// <param name="bufferCapacity">The initial buffer allocation metadata capacity, not a live allocation limit.</param>
    /// <exception cref="GPUException">A system-owned GPU resource could not be created.</exception>
    public RenderingSystem(GPUDevice device, ulong textureHeapByteCount,
        ulong bufferHeapByteCount = 64 * 1024 * 1024, uint bufferCapacity = 1024)
        : this(device, new HeapPoolDesc
        {
            InitialHeapSize = bufferHeapByteCount,
            MaxHeapSize = Math.Max(bufferHeapByteCount, 256UL * 1024 * 1024),
            InitialAllocationCapacity = bufferCapacity,
        }, new HeapPoolDesc
        {
            InitialHeapSize = textureHeapByteCount,
            MaxHeapSize = Math.Max(textureHeapByteCount, 256UL * 1024 * 1024),
        })
    {
    }

    /// <summary>Creates growable CPU-visible buffer and texture pools with independent budgets and the device's standard bindless descriptor heaps.</summary>
    /// <param name="device">The device; it must outlive this system.</param>
    /// <param name="bufferPool">The buffer storage growth policy and byte budget.</param>
    /// <param name="texturePool">The texture storage growth policy and byte budget, including the default white texture.</param>
    /// <exception cref="InvalidOperationException">The texture pool budget cannot accommodate the default white texture.</exception>
    /// <exception cref="GPUException">A system-owned GPU resource could not be created.</exception>
    public RenderingSystem(GPUDevice device, in HeapPoolDesc bufferPool, in HeapPoolDesc texturePool)
    {
        Debug.Assert(device != null);

        TextureHeapPool textures = new(device, texturePool);
        GPUHeapPool? buffers = null;
        GPUTextureDescriptorHeap? textureDescriptors = null;
        GPUSamplerDescriptorHeap? samplerDescriptors = null;
        UploadQueue? uploadQueue = null;
        Texture? textureWhite = null;
        SharedSamplers? samplers = null;
        try
        {
            buffers = _bufferPool = new GPUHeapPool(device, bufferPool);
            textureDescriptors = _textureDescriptors = device.CreateTextureDescriptorHeap();
            samplerDescriptors = _samplerDescriptors = device.CreateSamplerDescriptorHeap();

            _device = device;
            _texturePool = textures;
            _textureDescriptors = textureDescriptors;
            _samplerDescriptors = samplerDescriptors;
            _textureSlots = new DescriptorSlotAllocator(textureDescriptors.Capacity);
            _samplerSlots = new DescriptorSlotAllocator(samplerDescriptors.Capacity, SharedSamplers.Count);

            unsafe
            {
                uploadQueue = _uploadQueue = new UploadQueue(device, DefaultUploadStagingByteCount);
                _uploadQueue.SetSubmitCallback(SubmitUploadBatch);
            }
            _uploadTimeline = _uploadQueue.Flush();

            // Descriptor slot 0 retains the default white texture and linear-repeat sampler.
            textureWhite = _textureWhite = CreateTexture(new TextureDesc
            {
                Extent = new Extent3D(1, 1, 1),
                Format = Format.Rgba8Unorm,
                Usage = TextureUsage.Sampled | TextureUsage.TransferDestination,
            });
            InitializeTexture(textureWhite.Initialization);
            _uploadQueue.UploadTexture(textureWhite.NativeTexture, [0xff, 0xff, 0xff, 0xff]);
            samplers = _samplers = new SharedSamplers(this);
            _frameTimeline = device.CreateTimelineSemaphore();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            samplers?.Destroy();
            textureWhite?.Destroy();
            uploadQueue?.Wait();
            ReclaimTextures();
            uploadQueue?.Destroy();
            samplerDescriptors?.Destroy();
            textureDescriptors?.Destroy();
            buffers?.Destroy();
            textures.Destroy();
            throw;
        }
    }

    /// <summary>The device that owns the heaps and created resources.</summary>
    public GPUDevice Device => _device;

    /// <summary>The bindless texture descriptor heap that records bind through <see cref="GPUCommandBuffer.SetTextureDescriptorHeap"/>.</summary>
    public GPUTextureDescriptorHeap TextureDescriptorHeap => _textureDescriptors;

    /// <summary>The bindless sampler heap; slots owned by <see cref="Samplers"/> must not be overwritten.</summary>
    public GPUSamplerDescriptorHeap SamplerDescriptorHeap => _samplerDescriptors;

    /// <summary>The default white 1x1 texture; its contents are prepared with the system's automatic initialization.</summary>
    public Texture TextureWhite
    {
        get
        {
            Debug.Assert(!_destroyed);
            return _textureWhite;
        }
    }

    /// <summary>The system-owned linear-repeat sampler bound to unbound material sampler fields; do not destroy it.</summary>
    public Sampler SamplerDefault
    {
        get
        {
            Debug.Assert(!_destroyed);
            return _samplers.LinearRepeat;
        }
    }

    /// <summary>The system-owned shared sampler bank in fixed slots of <see cref="SamplerDescriptorHeap"/>.</summary>
    public SharedSamplers Samplers
    {
        get
        {
            Debug.Assert(!_destroyed);
            return _samplers;
        }
    }

    /// <summary>The lazily initialized shader system owning a default <see cref="ShaderCompiler"/> and interned shaders.</summary>
    public ShaderSystem Shaders
    {
        get
        {
            Debug.Assert(!_destroyed);
            ShaderSystem? existing = Volatile.Read(ref _shaders);
            if (existing != null)
                return existing;
            lock (_shaderLock)
            {
                _shaders ??= new ShaderSystem(this);
                return _shaders;
            }
        }
    }

    /// <summary>Controls how <see cref="RenderContext"/> handles uncached graphics or mesh pipelines during draw recording.</summary>
    public PipelineMissPolicy PipelineMissPolicy
    {
        get => (PipelineMissPolicy)Volatile.Read(ref _pipelineMissPolicy);
        set => Volatile.Write(ref _pipelineMissPolicy, (byte)value);
    }

    /// <summary>Creates an independent shader system with custom compiler options targeting this rendering system.</summary>
    /// <param name="options">Optional compiler search paths, defines, virtual file resolver, and cache settings.</param>
    /// <returns>A caller-owned <see cref="ShaderSystem"/>; dispose it before destroying this rendering system.</returns>
    public ShaderSystem CreateShaderSystem(ShaderCompilerOptions? options = null)
    {
        Debug.Assert(!_destroyed);
        return new ShaderSystem(this, options);
    }

    /// <summary>Copies CPU pixel data into system-owned staging and queues its texture upload on queue zero.</summary>
    /// <param name="texture">The destination texture created with <see cref="TextureUsage.TransferDestination"/>.</param>
    /// <param name="source">The region data including pitch padding; copied before this method returns.</param>
    /// <param name="copy">The destination mip, slices, region, and source memory pitches; default writes full mip zero.</param>
    public void UploadTexture(Texture texture, ReadOnlySpan<byte> source, in TextureCopyDesc copy = default)
    {
        Debug.Assert(!_destroyed && texture != null && !texture.IsDestroyed && texture.System == this);
        Debug.Assert((texture.Desc.Usage & TextureUsage.TransferDestination) != 0);
        if (source.IsEmpty)
            return;

        lock (_uploadLock)
        {
            if (texture.Initialization.Index >= 0)
                InitializeTexture(texture.Initialization);
            TextureUpload.UploadTexture(_uploadQueue, texture.NativeTexture, texture.Desc, source, copy);
            UploadQueueStats stats = _uploadQueue.Stats;
            texture.Initialization.Completion = new TimelinePoint
            {
                Semaphore = _uploadTimeline.Semaphore,
                Value = stats.PendingOperations > 0 ? stats.Submissions + 1 : stats.Submissions,
            };
        }
    }

    /// <summary>Submits pending texture initializations and system-queued uploads on queue zero; serialize this with other submissions to that queue.</summary>
    /// <returns>The completion borrowed until system destruction; include it in other queues' submission waits before texture use.</returns>
    /// <exception cref="GPUException">Upload command recording could not begin.</exception>
    public TimelinePoint FlushUploads()
    {
        Debug.Assert(!_destroyed);
        lock (_uploadLock)
        {
            while (_initializations.Count != 0)
                InitializeTexture(_initializations[^1]);
            _uploadQueue.Reclaim();
            return _uploadQueue.Flush();
        }
    }

    /// <summary>Submits pending texture initializations and system-queued uploads on queue zero; serialize this with other submissions to that queue.</summary>
    /// <returns>The completion borrowed until system destruction; include it in other queues' submission waits before texture use.</returns>
    /// <exception cref="GPUException">Initialization command recording could not begin.</exception>
    public TimelinePoint FlushInitializations() => FlushUploads();

    /// <summary>Ends and submits active frames in the given order without presenting or waiting for their completion.</summary>
    /// <param name="contexts">A nonempty list of distinct contexts from this system, each recording with no open scope.</param>
    /// <param name="waits">The GPU dependencies for this submission.</param>
    /// <returns>The shared GPU completion point, borrowing the system's timeline. Do not signal or destroy it.</returns>
    /// <remarks>Serialize access to the contexts and with other submissions on queue zero.</remarks>
    /// <exception cref="GPUException">Initialization command recording could not begin.</exception>
    public TimelinePoint Submit(scoped ReadOnlySpan<RenderContext> contexts, scoped ReadOnlySpan<TimelinePoint> waits = default)
    {
        Debug.Assert(!contexts.IsEmpty);
        return SubmitPrepared(contexts, null, waits, PrepareSubmission());
    }

    /// <summary>Ends and submits active frames in order and presents the specified swapchain image. Close all scopes first.</summary>
    /// <param name="contexts">A nonempty list of distinct contexts from this system, ordered with the acquiring context.</param>
    /// <param name="swapchain">The swapchain whose acquired image to present.</param>
    /// <param name="waits">The GPU dependencies for this submission.</param>
    /// <returns>The shared GPU completion point, borrowing the system's timeline. Do not signal or destroy it.</returns>
    /// <remarks>Serialize access to the contexts and with other submissions on queue zero.</remarks>
    /// <exception cref="GPUException">Initialization command recording could not begin.</exception>
    public TimelinePoint SubmitAndPresent(scoped ReadOnlySpan<RenderContext> contexts, GPUSwapchain swapchain,
        scoped ReadOnlySpan<TimelinePoint> waits = default)
    {
        Debug.Assert(!contexts.IsEmpty && swapchain != null);
        return SubmitPrepared(contexts, swapchain, waits, PrepareSubmission());
    }

    // Complete fallible texture initialization and queued uploads before associating a readback with the next signal.
    internal TimelinePoint PrepareSubmission()
    {
        FlushUploads();
        return new TimelinePoint { Semaphore = _frameTimeline, Value = _frameValue + 1 };
    }

    internal TimelinePoint SubmitPrepared(scoped ReadOnlySpan<RenderContext> contexts, GPUSwapchain? swapchain,
        scoped ReadOnlySpan<TimelinePoint> waits, TimelinePoint completion)
    {
        Debug.Assert(!_destroyed && !contexts.IsEmpty && completion.Semaphore == _frameTimeline && completion.Value == _frameValue + 1);
        if (_submitCommands.Length < contexts.Length)
            _submitCommands = new GPUCommandBuffer[contexts.Length];
        Span<GPUCommandBuffer> commands = _submitCommands.AsSpan(0, contexts.Length);
        try
        {
            for (int index = 0; index < contexts.Length; ++index)
            {
                RenderContext context = contexts[index];
                Debug.Assert(context != null && context.System == this);
                commands[index] = context.EndRecording();
            }
            SubmitDesc desc = new() { Commands = commands, Waits = waits, Completion = completion };
            lock (_queueZeroSubmitLock)
            {
                if (swapchain == null)
                    _device.Submit(desc);
                else
                    _device.SubmitAndPresent(desc, swapchain);
            }
        }
        finally
        {
            commands.Clear();
        }

        _frameValue = completion.Value;
        ulong submissionValue = OnSubmitted(completion);
        foreach (RenderContext context in contexts)
            context.RegisterSubmission(completion, submissionValue);
        return completion;
    }

    private unsafe void SubmitUploadBatch(void* context, GPUDevice device, GPUCommandBuffer commands, TimelinePoint completion, uint queueIndex)
    {
        lock (_queueZeroSubmitLock)
            device.Submit(new SubmitDesc { Commands = [commands], Completion = completion }, queueIndex);
    }

    private void InitializeTexture(Texture.InitializationState state)
    {
        _uploadQueue.InitializeTexture(state.Texture);
        UploadQueueStats stats = _uploadQueue.Stats;
        state.Completion = new TimelinePoint
        {
            Semaphore = _uploadTimeline.Semaphore,
            Value = stats.Submissions + 1,
        };
        RemoveInitialization(state);
    }

    private void RemoveInitialization(Texture.InitializationState state)
    {
        int index = state.Index;
        Texture.InitializationState last = _initializations[^1];
        _initializations[index] = last;
        last.Index = index;
        _initializations.RemoveAt(_initializations.Count - 1);
        state.Index = -1;
    }

    /// <summary>Allocates nonmoving buffer storage; its contents are initially undefined.</summary>
    /// <param name="byteCount">The positive capacity in bytes.</param>
    /// <returns>The buffer; destroy it after its final GPU use, or let collection reclaim it through <see cref="Tick"/>.</returns>
    /// <exception cref="InvalidOperationException">The buffer pool budget is exhausted.</exception>
    /// <exception cref="GPUException">A backing heap could not be created.</exception>
    public unsafe Buffer CreateBuffer(ulong byteCount)
    {
        Debug.Assert(!_destroyed && byteCount != 0);
        lock (_bufferLock)
        {
            HeapAllocation<byte> allocation = _bufferPool.Allocate(byteCount);
            if (allocation.Range.SizeBytes == 0)
                throw new InvalidOperationException("The rendering system's buffer pool budget is exhausted.");
            ++_liveBuffers;
            return new Buffer(this, allocation);
        }
    }

    /// <summary>Allocates nonmoving buffer storage and immediately copies the source into it.</summary>
    /// <typeparam name="T">The unmanaged source element type.</typeparam>
    /// <param name="source">The nonempty initial contents; the source may be released when this method returns.</param>
    /// <returns>The initialized buffer.</returns>
    /// <exception cref="InvalidOperationException">The buffer pool budget is exhausted.</exception>
    /// <exception cref="GPUException">A backing heap could not be created.</exception>
    public unsafe Buffer CreateBuffer<T>(ReadOnlySpan<T> source) where T : unmanaged
    {
        Debug.Assert(!source.IsEmpty);
        Buffer buffer = CreateBuffer((ulong)source.Length * (ulong)sizeof(T));
        buffer.SetData(source);
        return buffer;
    }

    /// <summary>Places a texture with full-chain sampled and per-mip storage descriptors; context frames submit its initialization automatically.</summary>
    /// <param name="desc">The texture description; Sampled allocates one slot and Storage allocates one slot per mip.</param>
    /// <returns>The texture wrapper; destroy it after its final GPU use, or let collection reclaim it through <see cref="Tick"/>.</returns>
    /// <exception cref="InvalidOperationException">The texture pool budget or descriptor heap is exhausted, or texture requirements are unsupported.
    /// </exception>
    /// <exception cref="GPUException">The texture or a backing heap could not be created.</exception>
    public Texture CreateTexture(in TextureDesc desc)
    {
        Debug.Assert(!_destroyed);
        PlacedTexture allocation = default;
        uint sampledIndex = Texture.NoIndex;
        uint storageIndex = Texture.NoIndex;
        uint[]? storageMipIndices = null;
        lock (_textureLock)
        {
            bool sampled = (desc.Usage & TextureUsage.Sampled) != 0;
            bool storage = (desc.Usage & TextureUsage.Storage) != 0;
            uint[]? storageMips = storage && desc.MipLevels > 1 ? new uint[(int)desc.MipLevels - 1] : null;
            Span<uint> storageMipSlots = storageMips;
            int allocatedStorageMipCount = 0;
            try
            {
                if (sampled)
                    sampledIndex = AllocateTextureSlot();
                if (storage)
                {
                    storageIndex = AllocateTextureSlot();
                    while (allocatedStorageMipCount < storageMipSlots.Length)
                    {
                        storageMipSlots[allocatedStorageMipCount] = AllocateTextureSlot();
                        ++allocatedStorageMipCount;
                    }
                }
                allocation = _texturePool.Allocate(desc);
                if (allocation.Texture == null)
                    throw new InvalidOperationException("The rendering system's texture pool budget is exhausted or the texture requirements are unsupported.");

                if (sampled)
                    _textureDescriptors.WriteTextureDescriptor(sampledIndex, allocation.Texture, TextureDescriptorType.Sampled, new TextureDescriptorDesc());
                if (storage)
                {
                    TextureDescriptorDesc descriptor = new() { MipCount = 1 };
                    _textureDescriptors.WriteTextureDescriptor(storageIndex, allocation.Texture, TextureDescriptorType.Storage, descriptor);
                    for (int index = 0; index < storageMipSlots.Length; ++index)
                    {
                        descriptor = new TextureDescriptorDesc { BaseMip = (uint)index + 1, MipCount = 1 };
                        _textureDescriptors.WriteTextureDescriptor(storageMipSlots[index], allocation.Texture, TextureDescriptorType.Storage, descriptor);
                    }
                }
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                if (sampledIndex != Texture.NoIndex)
                    _textureSlots.Free(sampledIndex);
                if (storageIndex != Texture.NoIndex)
                    _textureSlots.Free(storageIndex);
                for (int index = 0; index < allocatedStorageMipCount; ++index)
                    _textureSlots.Free(storageMipSlots[index]);
                _texturePool.Free(ref allocation);
                throw;
            }

            storageMipIndices = storageMips;
            ++_liveTextures;
        }
        Texture texture = new(this, desc, allocation, sampledIndex, storageIndex, storageMipIndices);
        lock (_uploadLock)
        {
            texture.Initialization.Index = _initializations.Count;
            _initializations.Add(texture.Initialization);
        }
        return texture;
    }

    /// <summary>Writes a sampler descriptor and returns its heap-backed wrapper.</summary>
    /// <param name="desc">The sampler description.</param>
    /// <returns>The sampler wrapper; destroy it after its final GPU use, or let collection reclaim it through <see cref="Tick"/>.</returns>
    /// <exception cref="InvalidOperationException">The sampler descriptor heap is exhausted.</exception>
    public Sampler CreateSampler(in SamplerDesc desc)
    {
        Debug.Assert(!_destroyed);
        lock (_samplerLock)
        {
            uint index = _samplerSlots.Allocate();
            if (index == Texture.NoIndex)
                throw new InvalidOperationException("The rendering system's sampler descriptor heap is exhausted.");
            try
            {
                return CreateSampler(index, desc);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                _samplerSlots.Free(index);
                throw;
            }
        }
    }

    internal Sampler CreateSampler(uint index, in SamplerDesc desc)
    {
        _samplerDescriptors.WriteSamplerDescriptor(index, desc);
        ++_liveSamplers;
        return new Sampler(this, desc, index);
    }

    /// <summary>Reclaims finalized wrappers after their registered uses complete; also called by <see cref="RenderContext.BeginFrame"/>.</summary>
    /// <remarks>Keep wrappers reachable until submission registration or discard. Material draws retain their bound resources until then.</remarks>
    public void Tick()
    {
        Debug.Assert(!_destroyed);
        if (Interlocked.CompareExchange(ref _ticking, 1, 0) != 0)
            return;
        lock (_uploadLock)
        {
            _uploadQueue.Reclaim();
            ReclaimTextures();
        }
        ulong completedValue;
        lock (_releaseLock)
        {
            if (_postedReleases != null)
            {
                foreach (PendingRelease entry in _postedReleases)
                    _pendingReleases.Add(entry);
                _postedReleases.Clear();
            }
            completedValue = _submissionValue;
            int pending = 0;
            for (int index = 0; index < _submissions.Count; ++index)
            {
                Submission entry = _submissions[index];
                bool completed = Completed(entry.Point);
                if (!completed)
                    completedValue = Math.Min(completedValue, entry.Value - 1);
                if (!completed)
                    _submissions[pending++] = entry;
            }
            _submissions.RemoveRange(pending, _submissions.Count - pending);
        }
        int keep = 0;
        for (int index = 0; index < _pendingReleases.Count; ++index)
        {
            PendingRelease entry = _pendingReleases[index];
            if (entry.RetireValue <= completedValue)
                entry.Target.Reclaim();
            else
                _pendingReleases[keep++] = entry;
        }
        _pendingReleases.RemoveRange(keep, _pendingReleases.Count - keep);
        Volatile.Write(ref _ticking, 0);
    }

    /// <summary>Destroys owned GPU resources after the device is idle; destroy other live wrappers first.</summary>
    public void Destroy()
    {
        if (_destroyed)
        {
            Debug.Assert(false);
            return;
        }
        DrainWarmUps();
        _shaders?.Dispose();
        _shaders = null;
        // The device is idle here: reclaim every finalized wrapper without waiting on the timeline.
        lock (_releaseLock)
        {
            if (_postedReleases != null)
            {
                foreach (PendingRelease entry in _postedReleases)
                    entry.Target.Reclaim();
                _postedReleases.Clear();
            }
        }
        foreach (PendingRelease entry in _pendingReleases)
            entry.Target.Reclaim();
        _pendingReleases.Clear();
        _uploadQueue.Wait();
        ReclaimTextures();
        _submissions.Clear();
        _frameTimeline.Destroy();
        _samplers.Destroy();
        _textureWhite.Destroy();
        _uploadQueue.Destroy();
        Debug.Assert(_liveBuffers == 0 && _liveTextures == 0 && _liveSamplers == 0);
        _samplerDescriptors.Destroy();
        _textureDescriptors.Destroy();
        _texturePool.Destroy();
        _bufferPool.Destroy();
        _destroyed = true;
    }

    internal void PostRelease(RenderingResource resource)
    {
        lock (_releaseLock)
        {
            _postedReleases ??= [];
            _postedReleases.Add(new PendingRelease { RetireValue = _submissionValue, Target = resource });
        }
    }

    internal ulong OnSubmitted(TimelinePoint completion)
    {
        lock (_releaseLock)
        {
            ulong value = ++_submissionValue;
            _submissions.Add(new Submission { Value = value, Point = completion });
            return value;
        }
    }

    internal void OnCompleted(ulong value)
    {
        if (value == 0)
            return;
        lock (_releaseLock)
        {
            for (int index = 0; index < _submissions.Count; ++index)
            {
                Submission entry = _submissions[index];
                if (entry.Value != value)
                    continue;
                _submissions[index] = _submissions[^1];
                _submissions.RemoveAt(_submissions.Count - 1);
                return;
            }
        }
    }

    internal void ReleaseBuffer(HeapAllocation<byte> allocation)
    {
        lock (_bufferLock)
        {
            Debug.Assert(!_destroyed && _liveBuffers != 0);
            _bufferPool.Free(allocation);
            --_liveBuffers;
        }
    }

    internal void ReleaseTexture(ref PlacedTexture allocation, uint sampledIndex, uint storageIndex, uint[]? storageMipIndices,
        Texture.InitializationState initialization)
    {
        lock (_uploadLock)
        {
            if (initialization.Index >= 0)
                RemoveInitialization(initialization);
            if (!Completed(initialization.Completion))
            {
                _textureReleases.Add(new PendingTextureRelease
                {
                    Allocation = allocation,
                    SampledIndex = sampledIndex,
                    StorageIndex = storageIndex,
                    StorageMipIndices = storageMipIndices,
                    Completion = initialization.Completion,
                });
                allocation = default;
                return;
            }
        }
        FreeTexture(ref allocation, sampledIndex, storageIndex, storageMipIndices);
    }

    private void ReclaimTextures()
    {
        int keep = 0;
        for (int index = 0; index < _textureReleases.Count; ++index)
        {
            PendingTextureRelease entry = _textureReleases[index];
            if (Completed(entry.Completion))
                FreeTexture(ref entry.Allocation, entry.SampledIndex, entry.StorageIndex, entry.StorageMipIndices);
            else
                _textureReleases[keep++] = entry;
        }
        _textureReleases.RemoveRange(keep, _textureReleases.Count - keep);
    }

    private void FreeTexture(ref PlacedTexture allocation, uint sampledIndex, uint storageIndex, ReadOnlySpan<uint> storageMipIndices)
    {
        lock (_textureLock)
        {
            Debug.Assert(!_destroyed && _liveTextures != 0 && allocation.Texture != null);
            if (sampledIndex != Texture.NoIndex)
                _textureSlots.Free(sampledIndex);
            if (storageIndex != Texture.NoIndex)
                _textureSlots.Free(storageIndex);
            foreach (uint index in storageMipIndices)
                _textureSlots.Free(index);
            _texturePool.Free(ref allocation);
            --_liveTextures;
        }
    }

    internal void ReleaseSampler(uint index)
    {
        lock (_samplerLock)
        {
            Debug.Assert(!_destroyed && _liveSamplers != 0);
            if (index >= SharedSamplers.Count)
                _samplerSlots.Free(index);
            --_liveSamplers;
        }
    }

    private uint AllocateTextureSlot()
    {
        uint index = _textureSlots.Allocate();
        if (index == Texture.NoIndex)
            throw new InvalidOperationException("The rendering system's texture descriptor heap is exhausted.");
        return index;
    }

    private static bool Completed(TimelinePoint point)
    {
        return point.Semaphore == null || point.Semaphore.CompletedValue >= point.Value;
    }

    private struct Submission
    {
        internal ulong Value;
        internal TimelinePoint Point;
    }

    private struct PendingTextureRelease
    {
        internal PlacedTexture Allocation;
        internal uint SampledIndex;
        internal uint StorageIndex;
        internal uint[]? StorageMipIndices;
        internal TimelinePoint Completion;
    }

    private struct PendingRelease
    {
        public ulong RetireValue;
        public RenderingResource Target;
    }

    // LIFO free list over a monotonically growing range: slots are recycled only after the wrapper is destroyed.
    private sealed class DescriptorSlotAllocator
    {
        private readonly uint[] _freeIndices;
        private uint _freeCount;
        private uint _nextIndex;

        internal DescriptorSlotAllocator(uint capacity, uint firstIndex = 0)
        {
            Debug.Assert(firstIndex <= capacity);
            _freeIndices = new uint[(int)capacity];
            _nextIndex = firstIndex;
        }

        internal uint Allocate()
        {
            if (_freeCount != 0)
                return _freeIndices[--_freeCount];
            if (_nextIndex == (uint)_freeIndices.Length)
                return Texture.NoIndex;
            return _nextIndex++;
        }

        internal void Free(uint index)
        {
            Debug.Assert(index < _nextIndex && _freeCount < (uint)_freeIndices.Length);
            _freeIndices[_freeCount++] = index;
        }
    }

    internal Task EnqueueShaderWarmUp(Shader shader, ShaderVariant? variant, string[]? normalizedSpecializations,
        in PipelineState state, in PipelineStateKey stateKey, in RenderPassSignature pass, in DepthStencilState depthStencil)
    {
        if (shader.Kind == ShaderKind.Compute)
            throw new InvalidOperationException($"Compute shader '{shader.Name}' has no raster pipeline.");
        if (shader.IsDestroyed || Volatile.Read(ref _warmupClosed))
            return Task.CompletedTask;

        ulong depthStencilKey = _device.ActiveBackend == GraphicsBackend.Dx12
            ? D3D12Pso.DepthStencilKey(in depthStencil, pass.DepthFormat != Format.Undefined, pass.StencilFormat != Format.Undefined)
            : 0;
        if (IsPipelineWarmed(variant, in pass, in stateKey, depthStencilKey))
            return Task.CompletedTask;

        WarmUpKey key = new(shader, variant?.Specializations ?? normalizedSpecializations!, stateKey, pass, depthStencilKey);
        lock (_warmupSync)
        {
            if (_warmupClosed || shader.IsDestroyed)
                return Task.CompletedTask;
            if (_pendingWarmups.TryGetValue(key, out Task? pending))
                return pending;
            if (IsPipelineWarmed(variant, in pass, in stateKey, depthStencilKey))
                return Task.CompletedTask;

            TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            WarmUpWorkItem item = new(this, key, variant, normalizedSpecializations, in state, in depthStencil, completion);
            _pendingWarmups.Add(key, completion.Task);
            try
            {
                ThreadPool.UnsafeQueueUserWorkItem(item, preferLocal: false);
            }
            catch
            {
                _pendingWarmups.Remove(key);
                throw;
            }
            return completion.Task;
        }
    }

    private static bool IsPipelineWarmed(ShaderVariant? variant, in RenderPassSignature pass, in PipelineStateKey stateKey, ulong depthStencilKey)
    {
        GPUPipeline? pipeline = variant?.TryGetCachedPipeline(in pass, in stateKey);
        return pipeline != null && (depthStencilKey == 0 || pipeline is D3D12Pso d3d12 && d3d12.HasDepthStencilVariants(depthStencilKey));
    }

    private void CompleteWarmUp(WarmUpKey key, TaskCompletionSource completion, Exception? failure)
    {
        lock (_warmupSync)
        {
            _pendingWarmups.Remove(key);
            if (failure != null)
                completion.SetException(failure);
            else
                completion.SetResult();
            if (_pendingWarmups.Count == 0)
                Monitor.PulseAll(_warmupSync);
        }
    }

    private void DrainWarmUps()
    {
        lock (_warmupSync)
        {
            Volatile.Write(ref _warmupClosed, true);
            while (_pendingWarmups.Count > 0)
                Monitor.Wait(_warmupSync);
        }
    }

    private readonly record struct WarmUpKey(Shader Shader, IReadOnlyList<string> Specializations,
        PipelineStateKey StateKey, RenderPassSignature Pass, ulong DepthStencilKey)
    {
        /// <inheritdoc />
        public bool Equals(WarmUpKey other)
        {
            if (Shader != other.Shader || !StateKey.Equals(other.StateKey) || !Pass.Equals(other.Pass) ||
                DepthStencilKey != other.DepthStencilKey || Specializations.Count != other.Specializations.Count)
                return false;
            for (int index = 0; index < Specializations.Count; ++index)
            {
                if (Specializations[index] != other.Specializations[index])
                    return false;
            }
            return true;
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            HashCode hash = new();
            hash.Add(Shader);
            hash.Add(StateKey);
            hash.Add(Pass);
            hash.Add(DepthStencilKey);
            for (int index = 0; index < Specializations.Count; ++index)
                hash.Add(Specializations[index]);
            return hash.ToHashCode();
        }
    }

    private sealed class WarmUpWorkItem : IThreadPoolWorkItem
    {
        private readonly RenderingSystem _system;
        private readonly WarmUpKey _key;
        private readonly ShaderVariant? _variant;
        private readonly string[]? _specializations;
        private readonly PipelineState _state;
        private readonly DepthStencilState _depthStencil;
        private readonly TaskCompletionSource _completion;

        public WarmUpWorkItem(RenderingSystem system, WarmUpKey key, ShaderVariant? variant, string[]? specializations,
            in PipelineState state, in DepthStencilState depthStencil, TaskCompletionSource completion)
        {
            _system = system;
            _key = key;
            _variant = variant;
            _specializations = specializations;
            _state = state;
            _depthStencil = depthStencil;
            _completion = completion;
        }

        /// <inheritdoc />
        public void Execute()
        {
            Exception? failure = null;
            try
            {
                if (Volatile.Read(ref _system._warmupClosed))
                    return;
                ShaderVariant? variant = _variant ?? _key.Shader.TryGetOrCompileVariant(_specializations!);
                variant?.WarmUpPipelineCore(in _state, _key.StateKey, _key.Pass, _depthStencil, _key.DepthStencilKey != 0);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                _system.CompleteWarmUp(_key, _completion, failure);
            }
        }
    }
}
