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
/// Results are cached weakly per (document, size): a texture stays alive only as long
/// as the caller holds a reference to it, and a collected or disposed texture is
/// re-rasterized on the next request. Cache lookups are safe from any thread; the
/// rasterization itself is serialized internally. The returned textures are owned by
/// the callers — disposing the renderer does not invalidate them.
/// </remarks>
public sealed class SvgRenderer : AutoDisposable
{
    /// <summary>The largest texture edge the renderer produces.</summary>
    public const uint MaxTextureSize = 4096;

    private const int InitialBufferCapacity = 1024;
    private const int SweepInterval = 64;

    private readonly RenderingSystem _renderingSystem;
    private readonly GPUDevice _device;
    private readonly GPUCommandBuffer _commandBuffer;
    private readonly ComputeMaterial _material;

    private GraphicsArrayBuffer<Vector4> _edges = null!;
    private GraphicsArrayBuffer<Vector4> _shapeBboxes = null!;
    private GraphicsArrayBuffer<uint4> _shapeMeta = null!;
    private GraphicsArrayBuffer<Vector4> _shapeColors = null!;
    private GraphicsArrayBuffer<Vector4> _gradientXforms = null!;
    private GraphicsArrayBuffer<Vector4> _gradientRamps = null!;

    private readonly object _rasterizeLock = new();
    private readonly ConcurrentDictionary<CacheKey, WeakReference<Texture2D>> _cache = new();
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
        _commandBuffer = _device.CreateCommandBuffer("svg_rasterize_command_buffer");
        _material = renderingSystem.CreateComputeMaterial(shader);

        _edges = renderingSystem.CreateGraphicsArrayBuffer<Vector4>(InitialBufferCapacity, "svg_edges");
        _shapeBboxes = renderingSystem.CreateGraphicsArrayBuffer<Vector4>(64, "svg_shape_bboxes");
        _shapeMeta = renderingSystem.CreateGraphicsArrayBuffer<uint4>(64, "svg_shape_meta");
        _shapeColors = renderingSystem.CreateGraphicsArrayBuffer<Vector4>(64, "svg_shape_colors");
        _gradientXforms = renderingSystem.CreateGraphicsArrayBuffer<Vector4>(32, "svg_gradient_xforms");
        _gradientRamps = renderingSystem.CreateGraphicsArrayBuffer<Vector4>(4096, "svg_gradient_ramps");

        _material.TrySetBuffer(ShaderResourceId.SvgEdges, _edges);
        _material.TrySetBuffer(ShaderResourceId.SvgBboxes, _shapeBboxes);
        _material.TrySetBuffer(ShaderResourceId.SvgMeta, _shapeMeta);
        _material.TrySetBuffer(ShaderResourceId.SvgColors, _shapeColors);
        _material.TrySetBuffer(ShaderResourceId.SvgGradientXforms, _gradientXforms);
        _material.TrySetBuffer(ShaderResourceId.SvgRamps, _gradientRamps);
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
        if (!NanoSvg.TryParse(svg, out NanoSvgImage? image) || image == null)
        {
            texture = null;
            return false;
        }

        uint width = requestedWidth;
        uint height = requestedHeight;
        if (width == 0 || height == 0)
        {
            uint intrinsicWidth = ClampSize(image.Width);
            uint intrinsicHeight = ClampSize(image.Height);
            width = width == 0 ? intrinsicWidth : width;
            height = height == 0 ? intrinsicHeight : height;
        }
        width = Math.Clamp(width, 1u, MaxTextureSize);
        height = Math.Clamp(height, 1u, MaxTextureSize);

        ulong hash = XxHash3.HashToUInt64(System.Text.Encoding.UTF8.GetBytes(svg));
        CacheKey key = new CacheKey(hash, width, height);
        if (_cache.TryGetValue(key, out WeakReference<Texture2D>? cached) &&
            cached.TryGetTarget(out Texture2D? alive) && !alive.IsDisposed)
        {
            texture = alive;
            return true;
        }

        SvgRasterizeData data = SvgGeometryUtility.Build(image, width, height);
        Texture2D result = _renderingSystem.CreateTexture2D(width, height, ImageLoadOption.Default with
        {
            Usage = TextureUsage.ComputeWrite,
            AddressMode = AddressMode.ClampToEdge,
            Name = $"svg_texture_{hash:X16}_{width}x{height}",
        });

        Rasterize(data, result);

        _cache[key] = new WeakReference<Texture2D>(result);
        MaybeSweep();

        texture = result;
        return true;
    }

    private static uint ClampSize(float value)
    {
        if (float.IsNaN(value) || value <= 0)
        {
            return 1;
        }
        return Math.Clamp((uint)MathF.Round(value), 1u, MaxTextureSize);
    }

    /// <summary>Uploads the payload and dispatches the rasterizer; queue operations on
    /// the device are not thread safe, so the whole recording runs under a lock.</summary>
    private void Rasterize(SvgRasterizeData data, Texture2D texture)
    {
        lock (_rasterizeLock)
        {
            EnsureBufferCapacity(data);

            UploadBuffer(_edges, CollectionsMarshal.AsSpan(data.Edges));
            UploadBuffer(_shapeBboxes, CollectionsMarshal.AsSpan(data.ShapeBboxes));
            UploadBuffer(_shapeMeta, CollectionsMarshal.AsSpan(data.ShapeMeta));
            UploadBuffer(_shapeColors, CollectionsMarshal.AsSpan(data.ShapeColors));
            UploadBuffer(_gradientXforms, CollectionsMarshal.AsSpan(data.GradientXforms));
            UploadBuffer(_gradientRamps, CollectionsMarshal.AsSpan(data.GradientRamps));

            _material.SetTexture2DStorage(ShaderResourceId.Output, texture, 0);

            PushConstants constants = new PushConstants
            {
                TexSize = new uint2(texture.Width, texture.Height),
                ShapeCount = (uint)data.ShapeMeta.Count,
            };

            _commandBuffer.Begin();
            using (GPUCommandBuffer.ComputePass computePass = _commandBuffer.BeginCompute())
            {
                _material.DispatchBySizeWithConstant(computePass, texture.Width, texture.Height, 1, constants);
            }
            _commandBuffer.End();
            _device.Submit(_commandBuffer);
        }
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

    private void EnsureBufferCapacity(SvgRasterizeData data)
    {
        // Buffers only grow; stale entries past the used count are never read.
        EnsureBuffer(ref _edges, data.Edges.Count, ShaderResourceId.SvgEdges, "svg_edges");
        EnsureBuffer(ref _shapeBboxes, data.ShapeBboxes.Count, ShaderResourceId.SvgBboxes, "svg_shape_bboxes");
        EnsureBuffer(ref _shapeMeta, data.ShapeMeta.Count, ShaderResourceId.SvgMeta, "svg_shape_meta");
        EnsureBuffer(ref _shapeColors, data.ShapeColors.Count, ShaderResourceId.SvgColors, "svg_shape_colors");
        EnsureBuffer(ref _gradientXforms, data.GradientXforms.Count, ShaderResourceId.SvgGradientXforms, "svg_gradient_xforms");
        EnsureBuffer(ref _gradientRamps, data.GradientRamps.Count, ShaderResourceId.SvgRamps, "svg_gradient_ramps");
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
        _material.TrySetBuffer(resourceId, buffer);
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

    /// <summary>
    /// Releases the GPU resources used by the SvgRenderer. Cached textures are owned
    /// by their callers and are not disposed here.
    /// </summary>
    /// <param name="disposing">true to release both managed and unmanaged resources; false to release only unmanaged resources.</param>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _edges.Dispose();
            _shapeBboxes.Dispose();
            _shapeMeta.Dispose();
            _shapeColors.Dispose();
            _gradientXforms.Dispose();
            _gradientRamps.Dispose();
            _material.Dispose();
            _commandBuffer.Dispose();
            _cache.Clear();
        }
    }
}
