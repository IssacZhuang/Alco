using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Alco.Graphics;
using Alco.Graphics.LowLevel;
using Alco.Rendering;

namespace Alco.Engine;

/// <summary>
/// Main-thread pipeline that turns a GPU texture into PNG bytes through asynchronous readback
/// and thread-pool encoding; callers must not block-await completion on the main thread.
/// </summary>
public sealed class PngReadbackPipeline : IDisposable
{
    private readonly GPUDevice _device;
    private readonly ReadbackRequest _readbackRequest = new();
    private readonly Func<RenderCaptureResult> _encodeFunc;

    private ReadbackQueue? _readbackQueue;
    private GPUCommandPool? _commandPool;
    private GPUTimelineSemaphore? _timeline;
    private ulong _completionValue;

    // NOTE: not 'readonly' — NativeBuffer<T> is a struct, and a readonly field would create a
    // defensive copy on every instance-method call.
    private NativeBuffer<byte> _readbackBuffer;

    private Task<RenderCaptureResult>? _encodeTask;
    private Action<RenderCaptureResult>? _onCompleted;
    private bool _readbackInFlight;
    private uint _width;
    private uint _height;
    private DateTimeOffset _capturedAtUtc;
    private long _readbackStartTimestamp;
    private double _readbackTimeMs;

    /// <summary>
    /// Gets or sets the staging heap capacity allocated when GPU readback is initialized.
    /// </summary>
    public ulong StagingCapacityBytes { get; set; } = 32ul * 1024 * 1024;

    /// <summary>Gets the encoding preset used for this pipeline's captures.</summary>
    public PngEncodeMode EncodingMode { get; init; } = PngEncodeMode.Balanced;

    /// <summary>
    /// Creates the pipeline.
    /// </summary>
    /// <param name="device">The graphics device readbacks submit to.</param>
    public PngReadbackPipeline(GPUDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        _device = device;
        _encodeFunc = Encode;
    }

    /// <summary>
    /// Whether a readback or encode is in flight.
    /// </summary>
    public bool IsBusy => _readbackInFlight || _encodeTask != null;

    /// <summary>
    /// Starts the asynchronous readback of an RGBA8 rendering texture.
    /// </summary>
    /// <param name="source">The texture to read. Must be RGBA8 (Unorm or Srgb) with a valid size.</param>
    /// <param name="onCompleted">Invoked from <see cref="Pump"/> with the finished result (success or failure).</param>
    /// <param name="failure">The failure result when the read could not start; null on success.</param>
    /// <returns>True when the readback was recorded and submitted; false with <paramref name="failure"/> set.</returns>
    public bool TryBeginRead(Texture source, Action<RenderCaptureResult> onCompleted, [NotNullWhen(false)] out RenderCaptureResult? failure)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(onCompleted);

        if (source.IsDestroyed)
        {
            failure = RenderCaptureResult.CreateFailure("Capture texture is destroyed.", nameof(ObjectDisposedException));
            return false;
        }

        return TryBeginRead(source.NativeTexture, source.Desc, onCompleted, out failure);
    }

    /// <summary>
    /// Starts the asynchronous readback of an RGBA8 GPU texture matching <paramref name="desc"/>.
    /// </summary>
    /// <param name="source">The texture to read. Must be RGBA8 (Unorm or Srgb) with copy-source usage.</param>
    /// <param name="desc">The texture description matching <paramref name="source"/>.</param>
    /// <param name="onCompleted">Invoked from <see cref="Pump"/> with the finished result (success or failure).</param>
    /// <param name="failure">The failure result when the read could not start; null on success.</param>
    /// <returns>True when the readback was recorded and submitted; false with <paramref name="failure"/> set.</returns>
    /// <exception cref="InvalidOperationException">A readback or encode is already in flight.</exception>
    public unsafe bool TryBeginRead(GPUTexture source, in TextureDesc desc, Action<RenderCaptureResult> onCompleted, [NotNullWhen(false)] out RenderCaptureResult? failure)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(onCompleted);

        if (IsBusy)
        {
            throw new InvalidOperationException("The PNG readback pipeline already has a capture in flight.");
        }

        uint width = desc.Extent.X;
        uint height = desc.Extent.Y;
        if (width == 0 || height == 0)
        {
            failure = RenderCaptureResult.CreateFailure("Capture texture has an invalid size.", nameof(InvalidOperationException));
            return false;
        }

        if (desc.Format is not (Format.Rgba8Unorm or Format.Rgba8Srgb))
        {
            failure = RenderCaptureResult.CreateFailure(
                $"Unsupported capture pixel format '{desc.Format}'. Expected {Format.Rgba8Unorm} or {Format.Rgba8Srgb}; convert on the GPU first.",
                nameof(NotSupportedException));
            return false;
        }

        ulong requiredBytes = (ulong)width * height * 4;
        if (!TryEnsureReadbackResources(requiredBytes, out failure))
        {
            return false;
        }

        int byteLength = checked((int)requiredBytes);
        _readbackBuffer.SetSizeWithoutCopy(byteLength);
        _readbackRequest.Reset();

        _width = width;
        _height = height;
        _capturedAtUtc = DateTimeOffset.UtcNow;
        _readbackStartTimestamp = Stopwatch.GetTimestamp();

        try
        {
            _commandPool!.Reset();
            GPUCommandBuffer commands = _commandPool.BeginCommands();
            commands.Barrier(Stage.AllCommands, Access.ShaderWrite | Access.TransferWrite | Access.ColorWrite, Stage.Transfer, Access.TransferRead);

            TimelinePoint completion = new() { Semaphore = _timeline, Value = _completionValue + 1 };
            bool accepted = _readbackQueue!.TryReadTexture(commands, source, in desc, completion, _readbackBuffer.UnsafePointer, requiredBytes, _readbackRequest);
            commands.EndCommands();

            if (!accepted)
            {
                _commandPool.Reset();
                failure = RenderCaptureResult.CreateFailure(
                    $"Readback staging capacity exhausted for {width}x{height} texture ({requiredBytes} bytes).",
                    nameof(InsufficientMemoryException));
                return false;
            }

            _completionValue = completion.Value;
            _device.Submit(new SubmitDesc { Commands = [commands], Completion = completion });
        }
        catch (Exception ex)
        {
            failure = RenderCaptureResult.CreateFailure($"Screenshot GPU readback failed: {ex.Message}", ex.GetType().Name);
            return false;
        }

        _readbackInFlight = true;
        _onCompleted = onCompleted;
        failure = null;
        return true;
    }

    /// <summary>
    /// Advances the pipeline on the main thread, starts a pending encode, and delivers finished
    /// captures to the callbacks registered with <see cref="TryBeginRead"/>.
    /// </summary>
    public void Pump()
    {
        if (CompleteFinishedEncode(out RenderCaptureResult? finished))
        {
            if (finished != null)
            {
                Deliver(finished);
            }

            return;
        }

        if (_readbackInFlight)
        {
            _readbackQueue?.Tick();
        }

        if (StartEncodeIfReadbackCompleted(out finished))
        {
            if (finished != null)
            {
                Deliver(finished);
            }
        }
    }

    private bool TryEnsureReadbackResources(ulong requiredBytes, [NotNullWhen(false)] out RenderCaptureResult? failure)
    {
        try
        {
            _commandPool ??= _device.CreateCommandPool(0);
            _timeline ??= _device.CreateTimelineSemaphore();
            if (_readbackQueue == null)
            {
                ulong alignedRequired = (requiredBytes + 15ul) & ~15ul;
                ulong capacity = Math.Max((StagingCapacityBytes + 15ul) & ~15ul, Math.Max(alignedRequired, 16ul));
                _readbackQueue = new ReadbackQueue(_device, capacity, maxPendingRequests: 4);
            }

            failure = null;
            return true;
        }
        catch (Exception ex)
        {
            failure = RenderCaptureResult.CreateFailure($"Failed to initialize GPU readback staging: {ex.Message}", ex.GetType().Name);
            return false;
        }
    }

    private void Deliver(RenderCaptureResult result)
    {
        Action<RenderCaptureResult>? callback = _onCompleted;
        _onCompleted = null;
        callback?.Invoke(result);
    }

    private bool StartEncodeIfReadbackCompleted(out RenderCaptureResult? failure)
    {
        failure = null;
        if (!_readbackInFlight || !_readbackRequest.IsCompleted)
        {
            return false;
        }

        _readbackInFlight = false;
        _readbackRequest.Reset();
        _readbackTimeMs = GetElapsedMilliseconds(_readbackStartTimestamp);

        try
        {
            _encodeTask = Task.Run(_encodeFunc);
        }
        catch (Exception ex)
        {
            failure = RenderCaptureResult.CreateFailure($"Screenshot PNG encode failed: {ex.Message}", ex.GetType().Name);
            return true;
        }

        return false;
    }

    private bool CompleteFinishedEncode(out RenderCaptureResult? result)
    {
        result = null;
        if (_encodeTask == null || !_encodeTask.IsCompleted)
        {
            return false;
        }

        Task<RenderCaptureResult> encodeTask = _encodeTask;
        _encodeTask = null;

        try
        {
            result = encodeTask.GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            result = RenderCaptureResult.CreateFailure($"Screenshot PNG encode failed: {ex.Message}", ex.GetType().Name);
        }

        return true;
    }

    private RenderCaptureResult Encode()
    {
        int width = checked((int)_width);
        int height = checked((int)_height);
        DateTimeOffset capturedAtUtc = _capturedAtUtc;
        double readbackTimeMs = _readbackTimeMs;

        long encodeStartTimestamp = Stopwatch.GetTimestamp();

        byte[] png = ImageEncodeUtility.EncodePng(_readbackBuffer.AsSpan(), width, height, EncodingMode);
        double encodeTimeMs = GetElapsedMilliseconds(encodeStartTimestamp);
        return RenderCaptureResult.CreateSuccess(png, width, height, capturedAtUtc, readbackTimeMs, encodeTimeMs);
    }

    private static double GetElapsedMilliseconds(long startTimestamp)
    {
        return (Stopwatch.GetTimestamp() - startTimestamp) * 1000.0 / Stopwatch.Frequency;
    }

    /// <summary>
    /// Waits for active readback/encode work and releases native staging resources.
    /// </summary>
    public void Dispose()
    {
        if (_encodeTask != null)
        {
            try
            {
                _encodeTask.Wait();
            }
            catch
            {
                // Dispose must not throw; owners surface failures through their own results.
            }

            _encodeTask = null;
        }

        if (_readbackInFlight || _readbackQueue != null)
        {
            _device.WaitIdle();
            _readbackInFlight = false;
        }

        _readbackQueue?.Destroy();
        _readbackQueue = null;
        _commandPool?.Destroy();
        _commandPool = null;
        _timeline?.Destroy();
        _timeline = null;
        _readbackRequest.Reset();
        _readbackBuffer.Dispose();
    }
}
