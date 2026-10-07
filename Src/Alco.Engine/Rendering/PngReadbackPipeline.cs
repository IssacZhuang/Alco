using System.Diagnostics;
using Alco;
using Alco.Graphics;
using Alco.Rendering;

namespace Alco.Engine;

/// <summary>
/// Frame-driven pipeline that turns a GPU texture into PNG bytes: asynchronous GPU
/// readback into a pooled CPU buffer, then a thread-pool PNG encode, surfaced as a
/// <see cref="RenderCaptureResult"/>. This is the shared tail of every "render
/// something, hand it out as PNG" system (render-graph captures, swapchain captures,
/// offscreen map snapshots, ...): the owner renders into its own
/// <see cref="RenderTexture"/>, calls <see cref="TryBeginRead"/> with a completion
/// callback when the pixels are ready, and the pipeline's pumper delivers the result
/// to that callback. The encoded PNG is always opaque (alpha is forced to 255 during
/// encode).
/// <br/>Readbacks only complete on the main thread's frame pump, so
/// <b>callers must not block-await their completion task on the main thread</b> — that
/// would stall the very pump this pipeline depends on. All methods are main-thread only.
/// </summary>
public sealed class PngReadbackPipeline
{
    private readonly GPUDevice _device;
    private readonly GPUTextureReadbackRequest _readbackRequest = new();
    private readonly Func<RenderCaptureResult> _encodeFunc;

    // NOTE: not 'readonly' — NativeBuffer<T> is a struct, and a readonly field would create a
    // defensive copy on every instance-method call, so SetSizeWithoutCopy/UnsafePointer would
    // operate on a throwaway copy and the field's pointer would stay null.
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
    /// Creates the pipeline.
    /// </summary>
    /// <param name="device">The graphics device readbacks submit to.</param>
    public PngReadbackPipeline(GPUDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        _device = device;
        _encodeFunc = Encode;
    }

    /// <summary>Whether a readback or encode is in flight; the next
    /// <see cref="Poll"/> has not produced its result yet.</summary>
    public bool IsBusy => _readbackInFlight || _encodeTask != null;

    /// <summary>
    /// Starts the asynchronous readback of a finished render texture's first color
    /// attachment. The texture's pixels must be final at this point (the one-shot copy
    /// submits immediately, so it lands between the submitting frame's present and the
    /// next frame's command buffer).
    /// </summary>
    /// <param name="source">The texture to read. Must be RGBA8 with a valid size.</param>
    /// <param name="onCompleted">Invoked from <see cref="Pump"/> with the finished result (success or failure).</param>
    /// <param name="failure">The failure result when the read could not start; null on success.</param>
    /// <returns>True when the readback was submitted; false with <paramref name="failure"/> set.</returns>
    /// <exception cref="InvalidOperationException">A readback is already in flight.</exception>
    public unsafe bool TryBeginRead(RenderTexture source, Action<RenderCaptureResult> onCompleted, [System.Diagnostics.CodeAnalysis.NotNullWhen(false)] out RenderCaptureResult? failure)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(onCompleted);

        if (source.IsDisposed)
        {
            failure = RenderCaptureResult.CreateFailure("Capture texture is disposed.", nameof(ObjectDisposedException));
            return false;
        }

        if (source.ColorCount <= 0)
        {
            failure = RenderCaptureResult.CreateFailure("Capture texture has no color attachment.", nameof(InvalidOperationException));
            return false;
        }

        uint width = source.Width;
        uint height = source.Height;
        if (width == 0 || height == 0)
        {
            failure = RenderCaptureResult.CreateFailure("Capture texture has an invalid size.", nameof(InvalidOperationException));
            return false;
        }

        Texture2D sourceTexture = source.ColorTextures[0];
        if (sourceTexture.IsDisposed)
        {
            failure = RenderCaptureResult.CreateFailure("Capture texture is disposed.", nameof(ObjectDisposedException));
            return false;
        }

        GPUTexture nativeTexture = sourceTexture.NativeTexture;
        return TryBeginRead(nativeTexture, onCompleted, out failure);
    }

    /// <summary>
    /// Starts the asynchronous readback of any RGBA8 GPU texture. Sources of another
    /// format must be converted first — blit them into an RGBA8 staging texture on the
    /// GPU (see <see cref="SwapchainCaptureSystem"/>), which is both faster than CPU
    /// pixel processing and format-generic.
    /// </summary>
    /// <param name="source">The texture to read. Must be RGBA8 with a valid size and copy-source usage.</param>
    /// <param name="onCompleted">Invoked from <see cref="Pump"/> with the finished result (success or failure).</param>
    /// <param name="failure">The failure result when the read could not start; null on success.</param>
    /// <returns>True when the readback was submitted; false with <paramref name="failure"/> set.</returns>
    /// <exception cref="InvalidOperationException">A readback is already in flight.</exception>
    public unsafe bool TryBeginRead(GPUTexture source, Action<RenderCaptureResult> onCompleted, [System.Diagnostics.CodeAnalysis.NotNullWhen(false)] out RenderCaptureResult? failure)
    {
        ArgumentNullException.ThrowIfNull(onCompleted);

        if (_readbackInFlight || _encodeTask != null)
        {
            throw new InvalidOperationException("The PNG readback pipeline already has a capture in flight.");
        }

        uint width = source.Width;
        uint height = source.Height;
        if (width == 0 || height == 0)
        {
            failure = RenderCaptureResult.CreateFailure("Capture texture has an invalid size.", nameof(InvalidOperationException));
            return false;
        }

        if (source.PixelFormat != PixelFormat.RGBA8Unorm)
        {
            failure = RenderCaptureResult.CreateFailure(
                $"Unsupported capture pixel format '{source.PixelFormat}'. Expected {PixelFormat.RGBA8Unorm}; convert on the GPU first.",
                nameof(NotSupportedException));
            return false;
        }

        int byteLength = checked((int)(width * height * 4));
        _readbackBuffer.SetSizeWithoutCopy(byteLength);
        _readbackRequest.Reset();

        _width = width;
        _height = height;
        _capturedAtUtc = DateTimeOffset.UtcNow;
        _readbackStartTimestamp = Stopwatch.GetTimestamp();
        _device.BeginReadTexture(source, _readbackBuffer.UnsafePointer, (uint)byteLength, _readbackRequest);
        _readbackInFlight = true;
        _onCompleted = onCompleted;
        failure = null;
        return true;
    }

    /// <summary>
    /// Pumps the pipeline once per update on the main thread: completes a finished
    /// encode, starts the encode when the readback completed, and delivers the finished
    /// capture's result to the callback registered with <see cref="TryBeginRead"/>.
    /// A shared pipeline can be pumped by a single pumper (the engine) and still route
    /// each result to its owning capture system.
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

        if (StartEncodeIfReadbackCompleted(out finished))
        {
            if (finished != null)
            {
                Deliver(finished);
            }

            return;
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
        try
        {
            _readbackRequest.ThrowIfFailed();
        }
        catch (Exception ex)
        {
            _readbackRequest.Reset();
            failure = RenderCaptureResult.CreateFailure(
                $"Screenshot GPU readback failed: {ex.Message}",
                ex.GetType().Name);
            return true;
        }

        _readbackTimeMs = GetElapsedMilliseconds(_readbackStartTimestamp);

        try
        {
            _encodeTask = Task.Run(_encodeFunc);
        }
        catch (Exception ex)
        {
            _readbackRequest.Reset();
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

        _readbackRequest.Reset();
        return true;
    }

    private unsafe RenderCaptureResult Encode()
    {
        nint pixelAddress = (nint)_readbackBuffer.UnsafePointer;
        int width = checked((int)_width);
        int height = checked((int)_height);
        DateTimeOffset capturedAtUtc = _capturedAtUtc;
        double readbackTimeMs = _readbackTimeMs;

        long encodeStartTimestamp = Stopwatch.GetTimestamp();
        // Screenshots are opaque: chain-tail blits may write non-1 alpha (the PBR deferred
        // blit writes ~0.74), and PNG viewers composite that transparency over their own
        // background, washing the colors out. Force every pixel's alpha to 255.
        byte* pixels = (byte*)pixelAddress;
        for (int i = 3; i < width * height * 4; i += 4)
        {
            pixels[i] = 255;
        }

        byte[] png = ImageEncodeUtility.EncodePng(pixels, width, height);
        double encodeTimeMs = GetElapsedMilliseconds(encodeStartTimestamp);
        return RenderCaptureResult.CreateSuccess(png, width, height, capturedAtUtc, readbackTimeMs, encodeTimeMs);
    }

    private static double GetElapsedMilliseconds(long startTimestamp)
    {
        return (Stopwatch.GetTimestamp() - startTimestamp) * 1000.0 / Stopwatch.Frequency;
    }

    /// <summary>
    /// Waits for an in-flight encode (so a background thread never touches a disposed
    /// buffer) and releases the readback buffer. In-flight readbacks are abandoned —
    /// owners resolve their callers' tasks with a failure themselves.
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

        _readbackRequest.Reset();
        _readbackBuffer.Dispose();
    }
}
