using System.Runtime.InteropServices;
using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// Streams image assets using background CPU decoding and frame-budgeted main-thread GPU uploads.
/// </summary>
public sealed unsafe class ImageUploadQueue : AutoDisposable
{
    /// <summary>Default per-frame GPU upload budget in bytes (8 MiB).</summary>
    public const ulong DefaultFrameByteBudget = 8ul * 1024 * 1024;

    private readonly RenderingSystem _renderingSystem;
    private readonly object _stateLock = new();
    private readonly Queue<PendingUpload> _pendingUploads = new();
    private PendingUpload? _activeUpload;
    private int _activeDecodeCount;
    private bool _queueDisposed;

    /// <summary>Gets the rendering system that owns the created GPU textures and upload staging queue.</summary>
    public RenderingSystem RenderingSystem => _renderingSystem;

    /// <summary>Gets or sets the per-frame byte budget consumed by <see cref="Pump"/>.</summary>
    public ulong FrameByteBudget { get; set; } = DefaultFrameByteBudget;

    /// <summary>Gets the number of decoded images waiting for or currently undergoing budgeted GPU upload.</summary>
    public int PendingUploadCount
    {
        get
        {
            lock (_stateLock)
            {
                return _pendingUploads.Count + (_activeUpload != null ? 1 : 0);
            }
        }
    }

    /// <summary>Gets the number of background CPU decode jobs currently in progress.</summary>
    public int ActiveDecodeCount
    {
        get
        {
            lock (_stateLock)
            {
                return _activeDecodeCount;
            }
        }
    }

    /// <summary>Gets whether any background decode or pending GPU upload remains.</summary>
    public bool HasPendingWork
    {
        get
        {
            lock (_stateLock)
            {
                return _activeDecodeCount > 0 || _pendingUploads.Count > 0 || _activeUpload != null;
            }
        }
    }

    /// <summary>Creates an image upload queue targeting <paramref name="renderingSystem"/>.</summary>
    /// <param name="renderingSystem">The rendering system used to allocate and upload textures.</param>
    public ImageUploadQueue(RenderingSystem renderingSystem)
    {
        ArgumentNullException.ThrowIfNull(renderingSystem);
        _renderingSystem = renderingSystem;
    }

    /// <summary>
    /// Probes an image stream header synchronously, creates the placeholder <see cref="Image"/>,
    /// and schedules background CPU decoding for later budgeted GPU upload in <see cref="Pump"/>.
    /// Stream ownership transfers on success; on header probe failure the caller retains ownership.
    /// </summary>
    /// <param name="stream">A seekable stream positioned at the start of the image file.</param>
    /// <param name="option">Optional import settings.</param>
    /// <param name="sprites">Optional named sprite regions in pixel coordinates.</param>
    /// <returns>A new <see cref="Image"/> whose <see cref="Image.ContentArrival"/> completes after upload or failure.</returns>
    /// <exception cref="ImageDecodeException">Unrecognized, truncated, or corrupt header.</exception>
    public Image CreateStreaming(
        Stream stream,
        ImageLoadOption? option = null,
        IReadOnlyDictionary<string, RectInt>? sprites = null)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        ArgumentNullException.ThrowIfNull(stream);

        ImageLoadOption optionReal = option ?? ImageLoadOption.Default;
        bool srgb = TextureUtility.IsSrgbFormat(optionReal.Format);
        ImageFileInfo info = ImageDecodeUtility.GetImageFileInfo(stream, srgb);

        Image? image = null;
        bool workerQueued = false;
        try
        {
            image = ImageUtility.CreateFromHeader(_renderingSystem, info, optionReal, sprites);
            TaskCompletionSource arrival = new(TaskCreationOptions.RunContinuationsAsynchronously);
            image.SetContentUpload(arrival.Task);

            lock (_stateLock)
            {
                ObjectDisposedException.ThrowIf(_queueDisposed || IsDisposed, this);
                _activeDecodeCount++;
            }

            try
            {
                ThreadPool.UnsafeQueueUserWorkItem(
                    new DecodeWorkItem(this, image, stream, optionReal, arrival),
                    preferLocal: false);
                workerQueued = true;
            }
            catch
            {
                FinishDecode(pending: null, arrival: null);
                throw;
            }

            return image;
        }
        catch
        {
            if (!workerQueued)
            {
                image?.Dispose();
                stream.Dispose();
            }
            throw;
        }
    }

    /// <summary>
    /// Uploads decoded image strips on the calling thread up to <paramref name="byteBudget"/> bytes.
    /// Always uploads at least one block-row when work is pending and <paramref name="byteBudget"/> is nonzero.
    /// </summary>
    /// <param name="byteBudget">Maximum bytes to queue this call, or <c>null</c> to use <see cref="FrameByteBudget"/>.</param>
    /// <returns>Total payload bytes queued into the rendering system during this call.</returns>
    public ulong Pump(ulong? byteBudget = null)
    {
        if (IsDisposed)
        {
            return 0;
        }

        ulong remainingBudget = byteBudget ?? FrameByteBudget;
        if (remainingBudget == 0)
        {
            return 0;
        }

        ulong uploadedBytes = 0;
        while (remainingBudget > 0)
        {
            PendingUpload? active;
            lock (_stateLock)
            {
                if (_queueDisposed)
                {
                    break;
                }
                if (_activeUpload == null && !_pendingUploads.TryDequeue(out _activeUpload))
                {
                    break;
                }
                active = _activeUpload;
            }

            bool finished = false;
            bool aborted = false;
            lock (active.Image.UploadLock)
            {
                if (active.Image.IsDisposed || active.Image.Texture.IsDisposed)
                {
                    aborted = true;
                }
                else
                {
                    try
                    {
                        UploadSlicesWithinBudget(active, ref remainingBudget, ref uploadedBytes);
                        if (active.CurrentMip >= active.MipLevels)
                        {
                            active.Image.MarkContentLoaded();
                            finished = true;
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Warning($"Failed to upload streamed image '{active.Image.Name}': {ex.Message}");
                        aborted = true;
                    }
                }
            }

            if (finished || aborted)
            {
                lock (_stateLock)
                {
                    if (ReferenceEquals(_activeUpload, active))
                    {
                        _activeUpload = null;
                    }
                }
                active.Dispose();
                active.Arrival.TrySetResult();
            }

            if (remainingBudget == 0 && !aborted)
            {
                break;
            }
        }

        return uploadedBytes;
    }

    private void UploadSlicesWithinBudget(PendingUpload active, ref ulong remainingBudget, ref ulong uploadedBytes)
    {
        while (active.CurrentMip < active.MipLevels)
        {
            uint mipWidth = Math.Max(1u, active.Width >> (int)active.CurrentMip);
            uint mipHeight = Math.Max(1u, active.Height >> (int)active.CurrentMip);
            uint blocksWide = (mipWidth + active.BlockWidth - 1) / active.BlockWidth;
            uint blocksHigh = (mipHeight + active.BlockHeight - 1) / active.BlockHeight;
            ulong blockRowBytes = (ulong)blocksWide * active.BytesPerBlock;

            uint remainingBlockRows = blocksHigh - active.CurrentBlockRow;
            ulong maxRowsBySpan = (ulong)int.MaxValue / blockRowBytes;
            ulong maxRowsByBudget = remainingBudget / blockRowBytes;
            uint rowsToUpload = (uint)Math.Min(remainingBlockRows, Math.Min(maxRowsByBudget, maxRowsBySpan));
            if (rowsToUpload == 0)
            {
                if (uploadedBytes > 0)
                {
                    remainingBudget = 0;
                    return;
                }
                // Guarantee forward progress when the caller's budget is smaller than a single block-row.
                rowsToUpload = 1;
            }

            ulong stripBytes = (ulong)rowsToUpload * blockRowBytes;
            uint offsetY = active.CurrentBlockRow * active.BlockHeight;
            uint stripHeightTexels = Math.Min(mipHeight - offsetY, rowsToUpload * active.BlockHeight);
            ulong sliceOffset = active.CurrentMipOffsetBytes + active.CurrentBlockRow * blockRowBytes;

            _renderingSystem.UploadTexture(
                active.Image.Texture,
                active.GetSlice(sliceOffset, checked((int)stripBytes)),
                new TextureCopyDesc
                {
                    MipLevel = active.CurrentMip,
                    Offset = new Extent3D(0, offsetY, 0),
                    Extent = new Extent3D(mipWidth, stripHeightTexels, 1),
                    RowPitchBytes = blockRowBytes,
                });

            uploadedBytes += stripBytes;
            remainingBudget = remainingBudget > stripBytes ? remainingBudget - stripBytes : 0;
            active.CurrentBlockRow += rowsToUpload;

            if (active.CurrentBlockRow == blocksHigh)
            {
                active.CurrentMipOffsetBytes += (ulong)blocksHigh * blockRowBytes;
                active.CurrentMip++;
                active.CurrentBlockRow = 0;
            }

            if (remainingBudget == 0)
            {
                return;
            }
        }
    }

    private void RunDecode(Image image, Stream stream, ImageLoadOption option, TaskCompletionSource arrival)
    {
        PendingUpload? pending = null;
        SafeMemoryHandle? fileData = null;
        try
        {
            using (stream)
            {
                if (IsShutdownOrImageDisposed(image))
                {
                    return;
                }

                fileData = new SafeMemoryHandle(stream.Length);
                stream.Position = 0;
                stream.ReadExactly(fileData.AsSpan());
            }

            if (IsShutdownOrImageDisposed(image))
            {
                return;
            }

            if ((image.Texture.Desc.Usage & TextureUsage.TransferDestination) == 0)
            {
                throw new InvalidOperationException("The texture is not writeable.");
            }

            ReadOnlySpan<byte> fileBytes = fileData.AsReadOnlySpan();
            if (DdsDecoder.IsDds(fileBytes))
            {
                bool srgb = TextureUtility.IsSrgbFormat(option.Format);
                DdsDecoder.Decode(
                    fileBytes,
                    srgb,
                    out DdsDecoder.BcFamily family,
                    out Format format,
                    out int width,
                    out int height,
                    out int mipLevels,
                    out int dataOffset);

                if (TextureUtility.TryGetBlockBytes(image.Format, out uint blockBytes))
                {
                    if (width != (int)image.Width || height != (int)image.Height
                        || format != image.Format || mipLevels != (int)image.MipLevels)
                    {
                        throw new ImageDecodeException(
                            $"DDS specification {width}x{height} {format} x{mipLevels} does not match the target image " +
                            $"{image.Width}x{image.Height} {image.Format} x{image.MipLevels}.");
                    }

                    pending = PendingUpload.CreateFromFileHandle(
                        image,
                        arrival,
                        (uint)width,
                        (uint)height,
                        (uint)mipLevels,
                        blockWidth: 4,
                        blockHeight: 4,
                        bytesPerBlock: blockBytes,
                        fileData,
                        dataOffset);
                    fileData = null;
                }
                else
                {
                    if (width != (int)image.Width || height != (int)image.Height || image.MipLevels != 1)
                    {
                        throw new ImageDecodeException(
                            $"DDS fallback specification {width}x{height} x1 does not match the target image " +
                            $"{image.Width}x{image.Height} x{image.MipLevels}.");
                    }

                    byte* fallbackPixels = BcDecoder.DecodeLevel(fileBytes, dataOffset, family, width, height, level: 0);
                    try
                    {
                        pending = PendingUpload.CreateFromNativePixels(image, arrival, (uint)width, (uint)height, fallbackPixels);
                        fallbackPixels = null;
                    }
                    finally
                    {
                        if (fallbackPixels != null)
                        {
                            NativeMemory.Free(fallbackPixels);
                        }
                    }
                }
            }
            else
            {
                ImageFileInfo info = ImageDecodeUtility.GetImageFileInfo(fileBytes);
                if (info.Width != (int)image.Width || info.Height != (int)image.Height)
                {
                    throw new ImageDecodeException(
                        $"Image specification {info.Width}x{info.Height} does not match the target image {image.Width}x{image.Height}.");
                }

                byte* pixels = ImageDecodeUtility.DecodeAuto(fileBytes, out int width, out int height);
                try
                {
                    if (option.PremultiplyAlpha)
                    {
                        ImageUtility.PremultiplyAlpha(pixels, width * height);
                    }
                    pending = PendingUpload.CreateFromNativePixels(image, arrival, (uint)width, (uint)height, pixels);
                    pixels = null;
                }
                finally
                {
                    if (pixels != null)
                    {
                        NativeMemory.Free(pixels);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning($"Failed to stream image '{image.Name}': {ex.Message}");
        }
        finally
        {
            fileData?.Dispose();
            FinishDecode(pending, arrival);
        }
    }

    private void FinishDecode(PendingUpload? pending, TaskCompletionSource? arrival)
    {
        bool completeArrival = false;
        lock (_stateLock)
        {
            if (pending != null && !_queueDisposed && !pending.Image.IsDisposed)
            {
                _pendingUploads.Enqueue(pending);
                pending = null;
            }
            else if (arrival != null)
            {
                completeArrival = true;
            }

            _activeDecodeCount--;
            if (_activeDecodeCount == 0 && _queueDisposed)
            {
                Monitor.PulseAll(_stateLock);
            }
        }

        if (pending != null)
        {
            pending.Dispose();
        }
        if (completeArrival && arrival != null)
        {
            arrival.TrySetResult();
        }
    }

    private bool IsShutdownOrImageDisposed(Image image)
    {
        if (image.IsDisposed || IsDisposed)
        {
            return true;
        }

        lock (_stateLock)
        {
            return _queueDisposed;
        }
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (!disposing)
        {
            return;
        }

        PendingUpload? active;
        List<PendingUpload>? queued = null;
        lock (_stateLock)
        {
            _queueDisposed = true;
            while (_activeDecodeCount > 0)
            {
                Monitor.Wait(_stateLock);
            }

            active = _activeUpload;
            _activeUpload = null;
            if (_pendingUploads.Count > 0)
            {
                queued = new List<PendingUpload>(_pendingUploads.Count);
                while (_pendingUploads.TryDequeue(out PendingUpload? item))
                {
                    queued.Add(item);
                }
            }
        }

        if (active != null)
        {
            active.Dispose();
            active.Arrival.TrySetResult();
        }

        if (queued != null)
        {
            for (int i = 0; i < queued.Count; i++)
            {
                PendingUpload item = queued[i];
                item.Dispose();
                item.Arrival.TrySetResult();
            }
        }
    }

    private sealed class DecodeWorkItem(
        ImageUploadQueue queue,
        Image image,
        Stream stream,
        ImageLoadOption option,
        TaskCompletionSource arrival) : IThreadPoolWorkItem
    {
        public void Execute() => queue.RunDecode(image, stream, option, arrival);
    }

    private sealed unsafe class PendingUpload : IDisposable
    {
        public readonly Image Image;
        public readonly TaskCompletionSource Arrival;
        public readonly uint Width;
        public readonly uint Height;
        public readonly uint MipLevels;
        public readonly uint BlockWidth;
        public readonly uint BlockHeight;
        public readonly uint BytesPerBlock;

        private readonly int _dataOffset;
        private SafeMemoryHandle? _ownedFileHandle;
        private byte* _ownedNativePixels;
        private int _disposed;

        public uint CurrentMip;
        public uint CurrentBlockRow;
        public ulong CurrentMipOffsetBytes;

        private PendingUpload(
            Image image,
            TaskCompletionSource arrival,
            uint width,
            uint height,
            uint mipLevels,
            uint blockWidth,
            uint blockHeight,
            uint bytesPerBlock,
            SafeMemoryHandle? ownedFileHandle,
            int dataOffset,
            byte* ownedNativePixels)
        {
            Image = image;
            Arrival = arrival;
            Width = width;
            Height = height;
            MipLevels = mipLevels;
            BlockWidth = blockWidth;
            BlockHeight = blockHeight;
            BytesPerBlock = bytesPerBlock;
            _ownedFileHandle = ownedFileHandle;
            _dataOffset = dataOffset;
            _ownedNativePixels = ownedNativePixels;
        }

        ~PendingUpload()
        {
            FreeUnmanaged();
        }

        public static PendingUpload CreateFromNativePixels(
            Image image,
            TaskCompletionSource arrival,
            uint width,
            uint height,
            byte* pixels)
        {
            return new PendingUpload(
                image,
                arrival,
                width,
                height,
                mipLevels: 1,
                blockWidth: 1,
                blockHeight: 1,
                bytesPerBlock: 4,
                ownedFileHandle: null,
                dataOffset: 0,
                ownedNativePixels: pixels);
        }

        public static PendingUpload CreateFromFileHandle(
            Image image,
            TaskCompletionSource arrival,
            uint width,
            uint height,
            uint mipLevels,
            uint blockWidth,
            uint blockHeight,
            uint bytesPerBlock,
            SafeMemoryHandle fileHandle,
            int dataOffset)
        {
            return new PendingUpload(
                image,
                arrival,
                width,
                height,
                mipLevels,
                blockWidth,
                blockHeight,
                bytesPerBlock,
                ownedFileHandle: fileHandle,
                dataOffset: dataOffset,
                ownedNativePixels: null);
        }

        public ReadOnlySpan<byte> GetSlice(ulong byteOffset, int byteLength)
        {
            if (_ownedFileHandle != null)
            {
                return _ownedFileHandle.AsReadOnlySpan().Slice(checked(_dataOffset + (int)byteOffset), byteLength);
            }

            return new ReadOnlySpan<byte>(_ownedNativePixels + byteOffset, byteLength);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            _ownedFileHandle?.Dispose();
            _ownedFileHandle = null;
            FreeUnmanaged();
            GC.SuppressFinalize(this);
        }

        private void FreeUnmanaged()
        {
            byte* pixels = _ownedNativePixels;
            if (pixels != null)
            {
                _ownedNativePixels = null;
                NativeMemory.Free(pixels);
            }
        }
    }
}
