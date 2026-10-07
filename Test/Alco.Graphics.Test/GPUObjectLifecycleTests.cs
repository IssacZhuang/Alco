using System.Runtime.CompilerServices;
using NUnit.Framework;

namespace Alco.Graphics.Test;

/// <summary>
/// Verifies managed GPU resource release and deferred-disposal queue shutdown without a native GPU.
/// </summary>
[TestFixture]
public sealed class GPUObjectLifecycleTests
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(10);

    /// <summary>Verifies shutdown ignores every remaining frame delay.</summary>
    /// <param name="delay">The configured deferred-release delay.</param>
    [TestCase(0u)]
    [TestCase(1u)]
    [TestCase(uint.MaxValue)]
    public void ShutdownDrainsEveryPendingObject(uint delay)
    {
        var host = new LifecycleHost();
        var device = new LifecycleDevice(host, delay);
        var first = new LifecycleObject(device);
        var second = new LifecycleObject(device);
        first.Dispose();
        second.Dispose();

        Assert.That(first.ReleaseCount, Is.Zero);
        host.Shutdown();

        Assert.That(first.ReleaseCount, Is.EqualTo(1));
        Assert.That(second.ReleaseCount, Is.EqualTo(1));
        Assert.That(first.ExplicitRelease, Is.True);
        Assert.That(device.DisposeCount, Is.EqualTo(1));
        Assert.That(host.HasHandlers, Is.False);
    }

    /// <summary>Verifies objects can release their own cleanup context after the device has closed.</summary>
    [Test]
    public void ClosedDeviceReleasesObjectsImmediately()
    {
        var host = new LifecycleHost();
        var device = new LifecycleDevice(host, uint.MaxValue);
        var resource = new LifecycleObject(device);
        host.Shutdown();

        resource.Dispose();
        resource.Dispose();
        device.Destroy(resource);

        Assert.That(resource.ReleaseCount, Is.EqualTo(1));
        Assert.That(resource.ExplicitRelease, Is.True);
        Assert.That(resource.IsDisposed, Is.True);
        Assert.That(host.HasHandlers, Is.False);
    }

    /// <summary>Verifies normal end-of-frame processing preserves the existing delay convention.</summary>
    /// <param name="delay">The number of frames to decrement before release becomes eligible.</param>
    [TestCase(0u)]
    [TestCase(1u)]
    [TestCase(3u)]
    public void EndFramePreservesConfiguredDelay(uint delay)
    {
        var host = new LifecycleHost();
        var device = new LifecycleDevice(host, delay);
        var resource = new LifecycleObject(device);
        resource.Dispose();

        for (uint i = 0; i < delay; i++)
        {
            host.EndFrame();
            Assert.That(resource.ReleaseCount, Is.Zero);
        }
        host.EndFrame();

        Assert.That(resource.ReleaseCount, Is.EqualTo(1));
        host.Shutdown();
        Assert.That(resource.ReleaseCount, Is.EqualTo(1));
    }

    /// <summary>Verifies frame cleanup releases the queue lock before disposing composite children.</summary>
    [Test]
    public void EndFrameReleasesOutsideQueueLock()
    {
        var host = new LifecycleHost();
        var device = new LifecycleDevice(host, 0);
        var child = new LifecycleObject(device);
        bool childScheduled = false;
        var parent = new LifecycleObject(device, onRelease: () =>
        {
            Task schedule = Task.Run(child.Dispose);
            childScheduled = schedule.Wait(WaitTimeout);
        });
        parent.Dispose();

        host.EndFrame();

        Assert.That(childScheduled, Is.True, "A different thread must be able to enqueue during cleanup.");
        Assert.That(parent.ReleaseCount, Is.EqualTo(1));
        Assert.That(child.ReleaseCount, Is.Zero, "Recursive disposal joins the next detached frame batch.");
        host.EndFrame();
        Assert.That(child.ReleaseCount, Is.EqualTo(1));
        host.Shutdown();
    }

    /// <summary>Verifies shutdown closes the queue before recursively releasing a composite object.</summary>
    [Test]
    public void ShutdownReleasesRecursiveChildrenImmediatelyOutsideQueueLock()
    {
        var host = new LifecycleHost();
        var device = new LifecycleDevice(host, uint.MaxValue);
        var child = new LifecycleObject(device);
        var parent = new LifecycleObject(device, onRelease: child.Dispose);
        bool parentReleased = false;
        var root = new LifecycleObject(device, onRelease: () =>
        {
            Task release = Task.Run(parent.Dispose);
            parentReleased = release.Wait(WaitTimeout);
        });
        root.Dispose();

        host.Shutdown();

        Assert.That(parentReleased, Is.True, "Recursive release must not hold the queue lock.");
        Assert.That(root.ReleaseCount, Is.EqualTo(1));
        Assert.That(parent.ReleaseCount, Is.EqualTo(1));
        Assert.That(child.ReleaseCount, Is.EqualTo(1));
        Assert.That(device.DisposeCount, Is.EqualTo(1));
    }

    /// <summary>Verifies the queue closes before backend shutdown callbacks can race with disposal.</summary>
    [Test]
    public void DisposalDuringShutdownCannotJoinAnAbandonedQueue()
    {
        var host = new LifecycleHost();
        var device = new LifecycleDevice(host, uint.MaxValue);
        var pending = new LifecycleObject(device);
        var racing = new LifecycleObject(device);
        pending.Dispose();
        using var entered = new ManualResetEventSlim();
        using var resume = new ManualResetEventSlim();
        device.EndFrameAction = () =>
        {
            entered.Set();
            if (!resume.Wait(WaitTimeout))
            {
                throw new TimeoutException("Shutdown callback was not resumed.");
            }
        };
        Task shutdown = Task.Run(host.Shutdown);
        try
        {
            Assert.That(entered.Wait(WaitTimeout), Is.True);
            racing.Dispose();
            Assert.That(racing.ReleaseCount, Is.EqualTo(1), "Closure must precede backend callbacks.");
        }
        finally
        {
            resume.Set();
            Assert.That(shutdown.Wait(WaitTimeout), Is.True);
        }
        Assert.That(pending.ReleaseCount, Is.EqualTo(1));
        Assert.That(device.DisposeCount, Is.EqualTo(1));
    }

    /// <summary>Verifies racing enqueue and closure always select a release path.</summary>
    [Test]
    public void EnqueueRacingWithClosureReleasesEveryObjectOnce()
    {
        for (int iteration = 0; iteration < 32; iteration++)
        {
            var host = new LifecycleHost();
            var device = new LifecycleDevice(host, uint.MaxValue);
            var resources = new LifecycleObject[32];
            for (int i = 0; i < resources.Length; i++)
            {
                resources[i] = new LifecycleObject(device);
            }
            using var start = new ManualResetEventSlim();
            Task enqueue = Task.Run(() =>
            {
                start.Wait();
                for (int i = 0; i < resources.Length; i++)
                {
                    resources[i].Dispose();
                }
            });
            Task shutdown = Task.Run(() =>
            {
                start.Wait();
                host.Shutdown();
            });
            start.Set();
            Assert.That(Task.WaitAll(new[] { enqueue, shutdown }, WaitTimeout), Is.True);

            for (int i = 0; i < resources.Length; i++)
            {
                Assert.That(resources[i].ReleaseCount, Is.EqualTo(1), $"Iteration {iteration}, object {i}.");
            }
            Assert.That(host.HasHandlers, Is.False);
        }
    }

    /// <summary>Verifies a resource cleanup failure cannot stop the rest of a detached batch.</summary>
    /// <param name="shutdown">Whether to release through shutdown instead of a normal frame.</param>
    [TestCase(false)]
    [TestCase(true)]
    public void CleanupFailureDoesNotStrandOtherEntries(bool shutdown)
    {
        var host = new LifecycleHost();
        var device = new LifecycleDevice(host, 0);
        var first = new LifecycleObject(device, onRelease: () => throw new InvalidOperationException("Cleanup failed."));
        var second = new LifecycleObject(device);
        var third = new LifecycleObject(device);
        first.Dispose();
        second.Dispose();
        third.Dispose();

        if (shutdown)
        {
            host.Shutdown();
        }
        else
        {
            host.EndFrame();
        }
        Assert.That(first.ReleaseCount, Is.EqualTo(1));
        Assert.That(second.ReleaseCount, Is.EqualTo(1));
        Assert.That(third.ReleaseCount, Is.EqualTo(1));
        Assert.That(host.ErrorCount, Is.EqualTo(1));
        device.DestroyImmediate(first);
        Assert.That(first.ReleaseCount, Is.EqualTo(1), "A failed release has still consumed its release claim.");
        host.Shutdown();
    }

    /// <summary>Verifies logging failure still permits all releases, backend teardown, and event detachment.</summary>
    [Test]
    public void ShutdownDetachesEventsEvenWhenErrorLoggingThrows()
    {
        var host = new LifecycleHost { ThrowOnLogError = true };
        var device = new LifecycleDevice(host, uint.MaxValue);
        var failing = new LifecycleObject(device, onRelease: () => throw new InvalidOperationException("Cleanup failed."));
        var succeeding = new LifecycleObject(device);
        failing.Dispose();
        succeeding.Dispose();

        Assert.Throws<InvalidOperationException>(host.Shutdown);

        Assert.That(failing.ReleaseCount, Is.EqualTo(1));
        Assert.That(succeeding.ReleaseCount, Is.EqualTo(1));
        Assert.That(device.DisposeCount, Is.EqualTo(1));
        Assert.That(host.HasHandlers, Is.False);
    }

    /// <summary>Verifies backend callback failures do not prevent draining and handler detachment.</summary>
    /// <param name="failEndFrame">Whether the end-of-frame callback rather than teardown throws.</param>
    [TestCase(false)]
    [TestCase(true)]
    public void ShutdownDetachesEventsAndDrainsWhenBackendThrows(bool failEndFrame)
    {
        var host = new LifecycleHost();
        var device = new LifecycleDevice(host, uint.MaxValue);
        var resource = new LifecycleObject(device);
        resource.Dispose();
        if (failEndFrame)
        {
            device.EndFrameAction = () => throw new InvalidOperationException("End frame failed.");
        }
        else
        {
            device.DisposeAction = () => throw new InvalidOperationException("Backend teardown failed.");
        }

        Assert.Throws<InvalidOperationException>(host.Shutdown);

        Assert.That(resource.ReleaseCount, Is.EqualTo(1));
        Assert.That(device.DisposeCount, Is.EqualTo(1));
        Assert.That(host.HasHandlers, Is.False);
    }

    /// <summary>Verifies failed scheduling can be retried rather than permanently marking the resource disposed.</summary>
    [Test]
    public void FailedSchedulingCanBeRetried()
    {
        var host = new LifecycleHost();
        var device = new LifecycleDevice(host, uint.MaxValue) { FailScheduling = true };
        var resource = new LifecycleObject(device);

        Assert.Throws<InvalidOperationException>(resource.Dispose);
        Assert.That(resource.IsDisposed, Is.False);
        Assert.That(resource.ReleaseCount, Is.Zero);
        device.FailScheduling = false;
        resource.Dispose();
        Assert.That(resource.IsDisposed, Is.True);
        host.Shutdown();

        Assert.That(resource.ReleaseCount, Is.EqualTo(1));
    }

    /// <summary>Verifies a throwing immediate release does not reset the consumed release claim.</summary>
    [Test]
    public void ClosedImmediateReleaseFailureStillClaimsExactlyOnce()
    {
        var host = new LifecycleHost();
        var device = new LifecycleDevice(host, 0);
        var resource = new LifecycleObject(device, onRelease: () => throw new InvalidOperationException("Cleanup failed."));
        host.Shutdown();

        Assert.Throws<InvalidOperationException>(resource.Dispose);
        Assert.That(resource.IsDisposed, Is.True);
        Assert.DoesNotThrow(resource.Dispose);
        Assert.That(resource.ReleaseCount, Is.EqualTo(1));
    }

    /// <summary>Verifies concurrent explicit disposal schedules the same object once.</summary>
    [Test]
    public void ConcurrentDisposeSchedulesOnce()
    {
        var host = new LifecycleHost();
        var device = new LifecycleDevice(host, uint.MaxValue);
        var resource = new LifecycleObject(device);

        Parallel.For(0, 128, _ => resource.Dispose());
        Assert.That(device.ScheduleCount, Is.EqualTo(1));
        host.Shutdown();

        Assert.That(resource.ReleaseCount, Is.EqualTo(1));
    }

    /// <summary>Verifies immediate destruction and an existing deferred queue entry share one release claim.</summary>
    [Test]
    public void ConcurrentImmediateDestructionAndDeferredDrainReleaseOnce()
    {
        var host = new LifecycleHost();
        var device = new LifecycleDevice(host, uint.MaxValue);
        var resource = new LifecycleObject(device);
        resource.Dispose();

        Parallel.For(0, 128, _ => device.DestroyImmediate(resource));
        host.Shutdown();

        Assert.That(resource.ReleaseCount, Is.EqualTo(1));
        Assert.That(resource.IsDisposed, Is.True);
    }

    /// <summary>Verifies abandoned resources use the non-disposing release path, including after a scheduling failure.</summary>
    /// <param name="failScheduling">Whether public disposal throws before the object is abandoned.</param>
    [TestCase(false)]
    [TestCase(true)]
    public void FinalizerReleasesWithoutExplicitDisposal(bool failScheduling)
    {
        var host = new LifecycleHost();
        var device = new LifecycleDevice(host, uint.MaxValue) { FailScheduling = failScheduling };
        var state = new ReleaseState();
        WeakReference abandoned = AbandonObject(device, state, failScheduling);

        CollectGarbage();

        Assert.That(state.Count, Is.EqualTo(1));
        Assert.That(state.Explicit, Is.False, "Finalization must not finish command recording.");
        Assert.That(abandoned.IsAlive, Is.False);
        host.Shutdown();
        GC.KeepAlive(device);
    }

    /// <summary>Verifies the finalizer checks actual release even when disposal was already scheduled.</summary>
    [Test]
    public void FinalizerUsesReleaseClaimRatherThanScheduledFlag()
    {
        var host = new LifecycleHost();
        var device = new LifecycleDevice(host, uint.MaxValue) { DropScheduledObjects = true };
        var state = new ReleaseState();
        WeakReference abandoned = AbandonScheduledObject(device, state);

        CollectGarbage();

        Assert.That(state.Count, Is.EqualTo(1));
        Assert.That(state.Explicit, Is.False);
        Assert.That(abandoned.IsAlive, Is.False);
        host.Shutdown();
        GC.KeepAlive(device);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference AbandonObject(LifecycleDevice device, ReleaseState state, bool failScheduling)
    {
        var resource = new LifecycleObject(device, state);
        if (failScheduling)
        {
            Assert.Throws<InvalidOperationException>(resource.Dispose);
        }
        return new WeakReference(resource);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference AbandonScheduledObject(LifecycleDevice device, ReleaseState state)
    {
        var resource = new LifecycleObject(device, state);
        resource.Dispose();
        // A synthetic scheduler drops its reference; re-register only to test the finalizer's guard.
        GC.ReRegisterForFinalize(resource);
        return new WeakReference(resource);
    }

    private static void CollectGarbage()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private sealed class ReleaseState
    {
        private int _count;
        internal int Count => Volatile.Read(ref _count);
        internal bool Explicit { get; private set; }

        internal void Release(bool disposing)
        {
            Explicit = disposing;
            Interlocked.Increment(ref _count);
        }
    }

    private sealed class LifecycleObject : BaseGPUObject
    {
        private readonly GPUDevice _device;
        private readonly ReleaseState _state;
        private readonly Action? _onRelease;

        protected override GPUDevice Device => _device;
        internal int ReleaseCount => _state.Count;
        internal bool ExplicitRelease => _state.Explicit;

        internal LifecycleObject(GPUDevice device, ReleaseState? state = null, Action? onRelease = null)
            : base("lifecycle_test")
        {
            _device = device;
            _state = state ?? new ReleaseState();
            _onRelease = onRelease;
        }

        protected override void Dispose(bool disposing)
        {
            _state.Release(disposing);
            _onRelease?.Invoke();
        }
    }

    private sealed class LifecycleHost : IGPUDeviceHost
    {
        private event Action? _endFrame;
        private event Action? _dispose;
        private int _errorCount;

        event Action IGPUDeviceHost.OnEndFrame
        {
            add => _endFrame += value;
            remove => _endFrame -= value;
        }

        event Action IGPUDeviceHost.OnDispose
        {
            add => _dispose += value;
            remove => _dispose -= value;
        }

        internal bool HasHandlers => _endFrame != null || _dispose != null;
        internal int ErrorCount => Volatile.Read(ref _errorCount);
        internal bool ThrowOnLogError { get; set; }
        internal void EndFrame() => _endFrame?.Invoke();
        internal void Shutdown() => _dispose?.Invoke();
        void IGPUDeviceHost.LogInfo(ReadOnlySpan<char> message) { }
        void IGPUDeviceHost.LogWarning(ReadOnlySpan<char> message) { }
        void IGPUDeviceHost.LogSuccess(ReadOnlySpan<char> message) { }

        void IGPUDeviceHost.LogError(ReadOnlySpan<char> message)
        {
            Interlocked.Increment(ref _errorCount);
            if (ThrowOnLogError)
            {
                throw new InvalidOperationException("Host logging failed.");
            }
        }
    }

    private sealed class LifecycleDevice : GPUDevice
    {
        private int _disposeCount;
        private int _scheduleCount;
        internal int DisposeCount => Volatile.Read(ref _disposeCount);
        internal int ScheduleCount => Volatile.Read(ref _scheduleCount);
        internal bool FailScheduling { get; set; }
        internal bool DropScheduledObjects { get; set; }
        internal Action? EndFrameAction { get; set; }
        internal Action? DisposeAction { get; set; }

        internal LifecycleDevice(LifecycleHost host, uint delay)
            : base(new DeviceDescriptor { Host = host, DisposeDelay = delay, Name = "lifecycle_test" })
        {
        }

        /// <summary>Gets the synthetic device's surface format.</summary>
        public override PixelFormat PreferredSurfaceFormat => PixelFormat.RGBA8Unorm;
        /// <summary>Gets the synthetic backend.</summary>
        public override GraphicsBackend Backend => GraphicsBackend.None;
        /// <summary>Gets the synthetic capabilities (none reported).</summary>
        public override GPUCapabilities Capabilities => GPUCapabilities.None;
        /// <summary>Gets the synthetic bind-group limit.</summary>
        public override int MaxBindGroups => 0;
        /// <summary>Gets the synthetic feature set.</summary>
        public override GPUFeatures SupportedFeatures => GPUFeatures.None;
        /// <summary>Gets the synthetic timestamp period.</summary>
        public override float TimestampPeriodNanoseconds => 0;
        /// <summary>Gets the unsupported uniform-buffer group.</summary>
        public override GPUBindGroup BindGroupUniformBuffer => throw new NotSupportedException();
        /// <summary>Gets the unsupported storage-buffer group.</summary>
        public override GPUBindGroup BindGroupStorageBuffer => throw new NotSupportedException();
        /// <summary>Gets the unsupported counter-buffer group.</summary>
        public override GPUBindGroup BindGroupStorageBufferWithCounter => throw new NotSupportedException();
        /// <summary>Gets the unsupported readable 2D texture group.</summary>
        public override GPUBindGroup BindGroupTexture2DRead => throw new NotSupportedException();
        /// <summary>Gets the unsupported writable 2D texture group.</summary>
        public override GPUBindGroup BindGroupTexture2DStorage => throw new NotSupportedException();
        /// <summary>Gets the unsupported readable 3D texture group.</summary>
        public override GPUBindGroup BindGroupTexture3DRead => throw new NotSupportedException();

        /// <summary>Optionally simulates scheduling failure before using the real deferred queue.</summary>
        /// <param name="obj">The resource to schedule.</param>
        public override void Destroy(BaseGPUObject obj)
        {
            Interlocked.Increment(ref _scheduleCount);
            if (FailScheduling)
            {
                throw new InvalidOperationException("Scheduling failed.");
            }
            if (!DropScheduledObjects)
            {
                base.Destroy(obj);
            }
        }

        protected override void OnEndFrameCore() => EndFrameAction?.Invoke();
        protected override void DisposeCore()
        {
            Interlocked.Increment(ref _disposeCount);
            DisposeAction?.Invoke();
        }

        protected override GPUBuffer CreateBufferCore(in BufferDescriptor descriptor) => throw new NotSupportedException();
        protected override GPUTexture CreateTextureCore(in TextureDescriptor descriptor) => throw new NotSupportedException();
        protected override GPUCommandBuffer CreateCommandBufferCore(in CommandBufferDescriptor? descriptor) => throw new NotSupportedException();
        protected override GPUTimestampQuerySet CreateTimestampQuerySetCore(uint count, string name) => throw new NotSupportedException();
        protected override GPURenderBundle CreateRenderBundleCore(in RenderBundleDescriptor? descriptor) => throw new NotSupportedException();
        protected override GPUAttachmentLayout CreateAttachmentLayoutCore(in AttachmentLayoutDescriptor descriptor) => throw new NotSupportedException();
        protected override GPUFrameBuffer CreateFrameBufferCore(in FrameBufferDescriptor descriptor) => throw new NotSupportedException();
        protected override GPUFrameBuffer CreateExternalFrameBufferCore(in ExternalFrameBufferDescriptor descriptor) => throw new NotSupportedException();
        protected override GPUPipeline CreateGraphicsPipelineCore(in GraphicsPipelineDescriptor descriptor) => throw new NotSupportedException();
        protected override GPUPipeline CreateComputePipelineCore(in ComputePipelineDescriptor descriptor) => throw new NotSupportedException();
        protected override GPUBindGroup CreateBindGroupCore(in BindGroupDescriptor descriptor) => throw new NotSupportedException();
        protected override GPUResourceGroup CreateResourceGroupCore(in ResourceGroupDescriptor descriptor) => throw new NotSupportedException();
        protected override GPUTextureView CreateTextureViewCore(in TextureViewDescriptor descriptor) => throw new NotSupportedException();
        protected override GPUSampler CreateSamplerCore(in SamplerDescriptor descriptor) => throw new NotSupportedException();

        /// <summary>Rejects native swap-chain creation on the synthetic device.</summary>
        /// <param name="descriptor">The unused swap-chain descriptor.</param>
        /// <returns>No swap chain; this method always throws.</returns>
        public override GPUSwapchain CreateSwapchainCore(in SwapchainDescriptor descriptor) => throw new NotSupportedException();

        protected override void SubmitCore(GPUCommandBuffer commandBuffer) => throw new NotSupportedException();
        protected override unsafe void WriteBufferCore(GPUBuffer buffer, uint bufferOffset, byte* data, uint size) => throw new NotSupportedException();
        protected override unsafe void ReadBufferCore(GPUBuffer buffer, byte* dest, uint bufferOffset, uint size) => throw new NotSupportedException();
        protected override unsafe void WriteTextureCore(GPUTexture texture, byte* data, uint dataSize, uint mipLevel) => throw new NotSupportedException();
        protected override unsafe void WriteTextureRegionCore(GPUTexture texture, byte* data, uint dataSize, uint bytesPerRow, uint x, uint y, uint width, uint height, uint mipLevel) => throw new NotSupportedException();
        protected override unsafe void ReadTextureCore(GPUTexture texture, byte* dest, uint dataSize, uint mipLevel) => throw new NotSupportedException();
        protected override unsafe void BeginReadTextureCore(GPUTexture texture, byte* dest, uint dataSize, GPUTextureReadbackRequest request, uint mipLevel) => throw new NotSupportedException();
        protected override void ProcessPendingReadbacksCore() { }
    }
}
