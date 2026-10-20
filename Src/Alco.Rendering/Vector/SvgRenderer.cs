using System.Collections.Concurrent;
using System.IO.Hashing;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Alco.Graphics;
using NanoSvgSharp;

namespace Alco.Rendering;

/// <summary>
/// Rasterizes SVG documents to <see cref="Texture2D"/> instances: parsing and geometry
/// preparation (curve flattening, dashing, stroke expansion) run on the CPU while
/// coverage evaluation, paints and compositing run in the SvgRasterize compute shader.
/// The output is a premultiplied-alpha RGBA8 texture.
/// </summary>
/// <remarks>
/// Results are cached weakly per (document hash, requested size): a texture stays alive
/// only as long as the caller holds a reference to it, and a collected or disposed
/// texture is re-rasterized on the next request. A cache hit hashes the document and
/// resolves the weak reference without any allocation — parsing, geometry and GPU work
/// happen on the first request only. All members are safe to call from any number of
/// threads: concurrent requests for the same cache key are deduplicated into a single
/// rasterization whose texture every waiter reuses, at most
/// <see cref="MaxConcurrentRasterizations"/> rasterizations execute at once (excess
/// requests queue for a slot instead of allocating more GPU state), and each execution
/// borrows a pooled command buffer, material and buffer set that are reused across
/// rasterizations. The returned textures are owned by the callers — disposing the
/// renderer does not invalidate them, but callers must let in-flight rasterizations
/// finish before disposing the renderer.
/// </remarks>
public sealed class SvgRenderer : AutoDisposable
{
    /// <summary>The largest texture edge the renderer produces.</summary>
    public const uint MaxTextureSize = 4096;

    /// <summary>
    /// Gets the maximum number of rasterizations that execute concurrently; requests
    /// beyond the limit wait for a free slot instead of creating additional GPU
    /// command buffers and upload buffers.
    /// </summary>
    public static int MaxConcurrentRasterizations => Environment.ProcessorCount;

    private const int SweepInterval = 64;

    private readonly RenderingSystem _renderingSystem;
    private readonly GPUDevice _device;
    private readonly Shader _shader;
    private readonly ConcurrentPool<RasterContext> _contextPool;
    private readonly SemaphoreSlim _rasterizeSlots = new(MaxConcurrentRasterizations, MaxConcurrentRasterizations);
    private readonly ConcurrentDictionary<CacheKey, WeakReference<Texture2D>> _cache = new();
    private readonly ConcurrentDictionary<CacheKey, TaskCompletionSource<Texture2D?>> _inFlight = new();
    private int _addsSinceSweep;

    private readonly record struct CacheKey(ulong Hash, uint Width, uint Height);

    [StructLayout(LayoutKind.Sequential)]
    private struct PushConstants
    {
        public uint2 TexSize;
        public uint ShapeCount;
        private readonly uint _pad;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SvgRenderer"/> class.
    /// </summary>
    /// <param name="renderingSystem">The rendering system instance.</param>
    /// <param name="shader">The SvgRasterize compute shader.</param>
    internal SvgRenderer(RenderingSystem renderingSystem, Shader shader)
    {
        _renderingSystem = renderingSystem;
        _device = renderingSystem.GraphicsDevice;
        _shader = shader;
        _contextPool = new ConcurrentPool<RasterContext>(CreateContext);
        // Warm the shader caches and keep one context ready so the first rasterization
        // does not pay the pipeline and bind-group setup cost.
        _contextPool.Return(_contextPool.Get());
    }

    /// <summary>
    /// Rasterizes an SVG document at its intrinsic size (the width/height attributes,
    /// or the viewBox size when they are absent).
    /// </summary>
    /// <param name="svg">The SVG document text.</param>
    /// <returns>The rasterized texture.</returns>
    /// <exception cref="ArgumentNullException">The document is null.</exception>
    /// <exception cref="ArgumentException">The document is not valid SVG.</exception>
    public Texture2D GetTexture(string svg)
    {
        return GetTexture(svg, 0, 0);
    }

    /// <summary>
    /// Rasterizes an SVG document into a texture of the requested size. The content is
    /// uniformly scaled to fit and centered, following the SVG default
    /// preserveAspectRatio meet rule.
    /// </summary>
    /// <param name="svg">The SVG document text.</param>
    /// <param name="width">The texture width in pixels; 0 uses the intrinsic size.</param>
    /// <param name="height">The texture height in pixels; 0 uses the intrinsic size.</param>
    /// <returns>The rasterized texture.</returns>
    /// <exception cref="ArgumentNullException">The document is null.</exception>
    /// <exception cref="ArgumentException">The document is not valid SVG.</exception>
    public Texture2D GetTexture(string svg, uint width, uint height)
    {
        ArgumentNullException.ThrowIfNull(svg);
        if (!TryGetOrCreate(svg, width, height, out Texture2D? texture))
        {
            throw new ArgumentException("The document is not valid SVG.", nameof(svg));
        }
        return texture!;
    }

    /// <summary>
    /// Attempts to rasterize an SVG document at its intrinsic size; parse failures
    /// return false instead of throwing.
    /// </summary>
    /// <param name="svg">The SVG document text.</param>
    /// <param name="texture">The rasterized texture, or null on failure.</param>
    /// <returns>true when the document rasterized into a texture.</returns>
    public bool TryGetTexture(string svg, out Texture2D? texture)
    {
        ArgumentNullException.ThrowIfNull(svg);
        return TryGetOrCreate(svg, 0, 0, out texture);
    }

    /// <summary>
    /// Attempts to rasterize an SVG document into a texture of the requested size;
    /// parse failures return false instead of throwing.
    /// </summary>
    /// <param name="svg">The SVG document text.</param>
    /// <param name="width">The texture width in pixels; 0 uses the intrinsic size.</param>
    /// <param name="height">The texture height in pixels; 0 uses the intrinsic size.</param>
    /// <param name="texture">The rasterized texture, or null on failure.</param>
    /// <returns>true when the document rasterized into a texture.</returns>
    public bool TryGetTexture(string svg, uint width, uint height, out Texture2D? texture)
    {
        ArgumentNullException.ThrowIfNull(svg);
        return TryGetOrCreate(svg, width, height, out texture);
    }

    private bool TryGetOrCreate(string svg, uint requestedWidth, uint requestedHeight, out Texture2D? texture)
    {
        // Hash and probe the cache before parsing: repeated requests for a live
        // texture (the hot path) must not allocate, so the document DOM is only
        // built on a miss. A key of 0 keeps the requested size: it means
        // "intrinsic size", resolved after parsing on the miss path.
        ulong hash = XxHash3.HashToUInt64(MemoryMarshal.AsBytes(svg.AsSpan()));
        CacheKey key = new CacheKey(
            hash,
            requestedWidth == 0 ? 0 : Math.Clamp(requestedWidth, 1u, MaxTextureSize),
            requestedHeight == 0 ? 0 : Math.Clamp(requestedHeight, 1u, MaxTextureSize));

        while (true)
        {
            if (TryGetLiveTexture(key, out texture))
            {
                return true;
            }

            // Single flight: the thread that installs the completion source rasterizes
            // for every waiter of the same key; the others wait for and reuse its
            // texture instead of duplicating the parse, geometry and GPU work.
            TaskCompletionSource<Texture2D?> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!_inFlight.TryAdd(key, completion))
            {
                if (!_inFlight.TryGetValue(key, out TaskCompletionSource<Texture2D?>? existing))
                {
                    // The entry completed and was removed before the lookup: retry,
                    // the cache probe runs first.
                    continue;
                }

                Texture2D? winner = existing.Task.GetAwaiter().GetResult();
                if (winner == null)
                {
                    // The winner rejected the document: propagate the parse failure.
                    texture = null;
                    return false;
                }
                if (!winner.IsDisposed)
                {
                    texture = winner;
                    return true;
                }
                // The winner's texture died immediately: fall through and retry.
                continue;
            }

            // This thread owns the rasterization and completes the in-flight entry on
            // every path, so the try/finally pair below never leaves waiters hanging.
            try
            {
                // Re-check the cache: an intrinsic-size rasterization of the same
                // document may have stored this exact key while the race was ongoing.
                if (TryGetLiveTexture(key, out texture))
                {
                    completion.TrySetResult(texture);
                    return true;
                }

                // Bound concurrent rasterizations: waiting here defers to the active
                // executions instead of growing the context pool.
                _rasterizeSlots.Wait();
                try
                {
                    texture = RasterizeUncached(svg, hash, key);
                    completion.TrySetResult(texture);
                    return texture != null;
                }
                finally
                {
                    _rasterizeSlots.Release();
                }
            }
            catch (Exception error)
            {
                completion.TrySetException(error);
                throw;
            }
            finally
            {
                _inFlight.TryRemove(key, out _);
            }
        }
    }

    /// <summary>Resolves a cache entry that still points at a live texture.</summary>
    private bool TryGetLiveTexture(CacheKey key, out Texture2D? texture)
    {
        if (_cache.TryGetValue(key, out WeakReference<Texture2D>? cached) &&
            cached.TryGetTarget(out Texture2D? alive) && !alive.IsDisposed)
        {
            texture = alive;
            return true;
        }
        texture = null;
        return false;
    }

    /// <summary>Runs the cache-miss path: parse, geometry build, GPU dispatch and
    /// cache store. Returns null when the document fails to parse. Must run under a
    /// rasterization slot.</summary>
    private Texture2D? RasterizeUncached(string svg, ulong hash, CacheKey key)
    {
        if (!NanoSvg.TryParse(svg, out NanoSvgImage? image) || image == null)
        {
            return null;
        }

        uint width = key.Width;
        uint height = key.Height;
        if (width == 0 || height == 0)
        {
            uint intrinsicWidth = ClampSize(image.Width);
            uint intrinsicHeight = ClampSize(image.Height);
            width = width == 0 ? intrinsicWidth : width;
            height = height == 0 ? intrinsicHeight : height;
        }

        SvgRasterizeData data = SvgGeometryUtility.Build(image, width, height);
        Texture2D result = _renderingSystem.CreateTexture2D(width, height, ImageLoadOption.Default with
        {
            Usage = TextureUsage.ComputeWrite,
            AddressMode = AddressMode.ClampToEdge,
            Name = $"svg_texture_{hash:X16}_{width}x{height}",
        });

        Rasterize(data, result);

        WeakReference<Texture2D> reference = new(result);
        _cache[key] = reference;
        if (key.Width == 0 || key.Height == 0)
        {
            // An intrinsic-size rasterization also answers explicit requests for the
            // resolved dimensions, preserving entry sharing with the resolved-size key.
            _cache[new CacheKey(hash, width, height)] = reference;
        }
        MaybeSweep();

        return result;
    }

    private static uint ClampSize(float value)
    {
        if (float.IsNaN(value) || value <= 0)
        {
            return 1;
        }
        return Math.Clamp((uint)MathF.Round(value), 1u, MaxTextureSize);
    }

    /// <summary>Uploads the payload and dispatches the rasterizer on a pooled context.
    /// Recording runs without an engine-side lock: queue buffer writes and distinct
    /// command buffers are concurrency-safe per the device contract, and the device
    /// serializes the native submit internally.</summary>
    private void Rasterize(SvgRasterizeData data, Texture2D texture)
    {
        RasterContext context = _contextPool.Get();
        try
        {
            context.EnsureCapacity(data);
            context.Upload(data);

            context.Material.SetTexture2DStorage(ShaderResourceId.Output, texture, 0);
            PushConstants constants = new PushConstants
            {
                TexSize = new uint2(texture.Width, texture.Height),
                ShapeCount = (uint)data.ShapeMeta.Count,
            };

            context.CommandBuffer.Begin();
            using (GPUCommandBuffer.ComputePass computePass = context.CommandBuffer.BeginCompute())
            {
                context.Material.DispatchBySizeWithConstant(computePass, texture.Width, texture.Height, 1, constants);
            }
            context.CommandBuffer.End();
            _device.Submit(context.CommandBuffer);
        }
        finally
        {
            _contextPool.Return(context);
        }
    }

    private void MaybeSweep()
    {
        if (Interlocked.Increment(ref _addsSinceSweep) < SweepInterval)
        {
            return;
        }
        _addsSinceSweep = 0;
        foreach (KeyValuePair<CacheKey, WeakReference<Texture2D>> entry in _cache)
        {
            if (!entry.Value.TryGetTarget(out Texture2D? target) || target.IsDisposed)
            {
                _cache.TryRemove(entry.Key, out _);
            }
        }
    }

    private RasterContext CreateContext()
    {
        return new RasterContext(_renderingSystem, _shader);
    }

    /// <summary>
    /// Releases the GPU resources used by the SvgRenderer. Cached textures are owned
    /// by their callers and are not disposed here.
    /// </summary>
    /// <param name="disposing">true to release both managed and unmanaged resources; false to release only unmanaged resources.</param>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (RasterContext context in _contextPool)
            {
                context.Dispose();
            }
            _contextPool.Clear();
            _rasterizeSlots.Dispose();
            _cache.Clear();
        }
    }

    /// <summary>
    /// The mutable GPU state a single rasterization needs: a dedicated command buffer,
    /// compute material and structured-buffer set. Contexts are pooled and reused
    /// (buffers only grow) and are borrowed by one rasterization at a time, so their
    /// use needs no engine-side lock.
    /// </summary>
    private sealed class RasterContext : IDisposable
    {
        private const int InitialEdgeCapacity = 1024;

        private readonly RenderingSystem _renderingSystem;

        /// <summary>The command buffer the rasterization dispatch is recorded on.</summary>
        public readonly GPUCommandBuffer CommandBuffer;

        /// <summary>The material binding the structured buffers and the output texture.</summary>
        public readonly ComputeMaterial Material;

        /// <summary>The flat edge list (x0, y0, x1, y1 per segment); grows only.</summary>
        public GraphicsArrayBuffer<Vector4> Edges;

        /// <summary>The per-shape bounding boxes; grows only.</summary>
        public GraphicsArrayBuffer<Vector4> ShapeBboxes;

        /// <summary>The per-shape metadata (edge range, fill rule, paint); grows only.</summary>
        public GraphicsArrayBuffer<uint4> ShapeMeta;

        /// <summary>The per-shape solid colors; grows only.</summary>
        public GraphicsArrayBuffer<Vector4> ShapeColors;

        /// <summary>The per-gradient transforms; grows only.</summary>
        public GraphicsArrayBuffer<Vector4> GradientXforms;

        /// <summary>The baked 64-entry gradient ramps; grows only.</summary>
        public GraphicsArrayBuffer<Vector4> GradientRamps;

        /// <summary>
        /// Initializes a new instance of the <see cref="RasterContext"/> class with
        /// fresh GPU resources bound to a fresh material.
        /// </summary>
        /// <param name="renderingSystem">The rendering system instance.</param>
        /// <param name="shader">The SvgRasterize compute shader.</param>
        public RasterContext(RenderingSystem renderingSystem, Shader shader)
        {
            _renderingSystem = renderingSystem;
            CommandBuffer = renderingSystem.GraphicsDevice.CreateCommandBuffer("svg_rasterize_command_buffer");
            Material = renderingSystem.CreateComputeMaterial(shader);

            Edges = renderingSystem.CreateGraphicsArrayBuffer<Vector4>(InitialEdgeCapacity, "svg_edges");
            ShapeBboxes = renderingSystem.CreateGraphicsArrayBuffer<Vector4>(64, "svg_shape_bboxes");
            ShapeMeta = renderingSystem.CreateGraphicsArrayBuffer<uint4>(64, "svg_shape_meta");
            ShapeColors = renderingSystem.CreateGraphicsArrayBuffer<Vector4>(64, "svg_shape_colors");
            GradientXforms = renderingSystem.CreateGraphicsArrayBuffer<Vector4>(32, "svg_gradient_xforms");
            GradientRamps = renderingSystem.CreateGraphicsArrayBuffer<Vector4>(4096, "svg_gradient_ramps");

            Material.TrySetBuffer(ShaderResourceId.SvgEdges, Edges);
            Material.TrySetBuffer(ShaderResourceId.SvgBboxes, ShapeBboxes);
            Material.TrySetBuffer(ShaderResourceId.SvgMeta, ShapeMeta);
            Material.TrySetBuffer(ShaderResourceId.SvgColors, ShapeColors);
            Material.TrySetBuffer(ShaderResourceId.SvgGradientXforms, GradientXforms);
            Material.TrySetBuffer(ShaderResourceId.SvgRamps, GradientRamps);
        }

        /// <summary>Grows the buffers to fit the payload. Stale entries past the used
        /// count are never read, so buffers only grow.</summary>
        /// <param name="data">The payload about to be uploaded.</param>
        public void EnsureCapacity(SvgRasterizeData data)
        {
            EnsureBuffer(ref Edges, data.Edges.Count, ShaderResourceId.SvgEdges, "svg_edges");
            EnsureBuffer(ref ShapeBboxes, data.ShapeBboxes.Count, ShaderResourceId.SvgBboxes, "svg_shape_bboxes");
            EnsureBuffer(ref ShapeMeta, data.ShapeMeta.Count, ShaderResourceId.SvgMeta, "svg_shape_meta");
            EnsureBuffer(ref ShapeColors, data.ShapeColors.Count, ShaderResourceId.SvgColors, "svg_shape_colors");
            EnsureBuffer(ref GradientXforms, data.GradientXforms.Count, ShaderResourceId.SvgGradientXforms, "svg_gradient_xforms");
            EnsureBuffer(ref GradientRamps, data.GradientRamps.Count, ShaderResourceId.SvgRamps, "svg_gradient_ramps");
        }

        /// <summary>Copies the payload into the staging spans and queues the GPU
        /// buffer writes. Queue buffer writes are concurrency-safe per the device
        /// contract and consume the staging memory before returning.</summary>
        /// <param name="data">The payload to upload.</param>
        public void Upload(SvgRasterizeData data)
        {
            UploadBuffer(Edges, CollectionsMarshal.AsSpan(data.Edges));
            UploadBuffer(ShapeBboxes, CollectionsMarshal.AsSpan(data.ShapeBboxes));
            UploadBuffer(ShapeMeta, CollectionsMarshal.AsSpan(data.ShapeMeta));
            UploadBuffer(ShapeColors, CollectionsMarshal.AsSpan(data.ShapeColors));
            UploadBuffer(GradientXforms, CollectionsMarshal.AsSpan(data.GradientXforms));
            UploadBuffer(GradientRamps, CollectionsMarshal.AsSpan(data.GradientRamps));
        }

        private void EnsureBuffer<T>(ref GraphicsArrayBuffer<T> buffer, int required, string resourceId, string name) where T : unmanaged
        {
            if (buffer.Length >= required)
            {
                return;
            }
            int newSize = buffer.Length;
            while (newSize < required)
            {
                newSize *= 2;
            }
            buffer.Dispose();
            buffer = _renderingSystem.CreateGraphicsArrayBuffer<T>(newSize, name);
            Material.TrySetBuffer(resourceId, buffer);
        }

        private static void UploadBuffer<T>(GraphicsArrayBuffer<T> buffer, ReadOnlySpan<T> data) where T : unmanaged
        {
            if (data.Length == 0)
            {
                return;
            }
            data.CopyTo(buffer.AsSpan());
            buffer.UpdateBuffer();
        }

        /// <summary>
        /// Releases the command buffer, material and structured buffers.
        /// </summary>
        public void Dispose()
        {
            Edges.Dispose();
            ShapeBboxes.Dispose();
            ShapeMeta.Dispose();
            ShapeColors.Dispose();
            GradientXforms.Dispose();
            GradientRamps.Dispose();
            Material.Dispose();
            CommandBuffer.Dispose();
        }
    }
}
