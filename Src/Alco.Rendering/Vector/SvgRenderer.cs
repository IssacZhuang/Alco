using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Alco.Graphics;
using NanoSvgSharp;
using Buffer = Alco.Graphics.Buffer;

namespace Alco.Rendering;

/// <summary>Compute-based bindless SVG vector rasterizer outputting premultiplied RGBA8 textures.</summary>
public sealed class SvgRenderer : AutoDisposable
{
    /// <summary>Maximum texture edge length supported for rasterized SVG icons.</summary>
    public const uint MaxTextureSize = 2048;

    private const ulong MinGeometryBytes = 4096;

    private struct PooledBuffer(RenderContext? context, ulong frameId, Buffer buffer)
    {
        public RenderContext? Context = context;
        public ulong FrameId = frameId;
        public Buffer Buffer = buffer;
    }

    private readonly RenderingSystem _system;
    private readonly Shader _shader;
    private readonly Material _material;
    private readonly List<PooledBuffer> _geometryPool = new(4);

    /// <summary>Creates a bindless GPU SVG rasterizer tied to <paramref name="system"/>.</summary>
    /// <param name="system">The rendering system owning shaders, textures, and transient buffers.</param>
    /// <param name="shaders">Optional shader system override; defaults to <see cref="RenderingSystem.Shaders"/>.</param>
    public SvgRenderer(RenderingSystem system, ShaderSystem? shaders = null)
    {
        _system = system ?? throw new ArgumentNullException(nameof(system));
        string source = LoadEmbeddedShaderSource("SvgRasterize.slang");
        ShaderSystem resolvedShaders = shaders ?? system.Shaders;
        _shader = resolvedShaders.GetShaderFromSource("SvgRasterize", "SvgRasterize.slang", source, ShaderKind.Compute);
        _material = _shader.CreateMaterial("SvgRasterize");
    }

    /// <summary>Parses SVG XML and builds flattened CPU geometry for <paramref name="width"/>x<paramref name="height"/>.</summary>
    /// <param name="svgXml">Raw SVG XML source text.</param>
    /// <param name="width">Target texture width in pixels.</param>
    /// <param name="height">Target texture height in pixels.</param>
    /// <param name="data">The constructed GPU rasterization payload when parsing succeeds.</param>
    /// <returns><see langword="true"/> when parsing and geometry flattening succeeded.</returns>
    public static bool TryParseAndBuild(string svgXml, uint width, uint height, out SvgRasterizeData data)
    {
        if (string.IsNullOrWhiteSpace(svgXml) || width == 0 || height == 0 ||
            !NanoSvg.TryParse(svgXml, "px", 96f, out NanoSvgImage? image) || image == null)
        {
            data = null!;
            return false;
        }

        data = SvgGeometryUtility.Build(image, width, height);
        return true;
    }

    /// <summary>Allocates a premultiplied <see cref="Format.Rgba8Unorm"/> target texture ready for compute output and sampling.</summary>
    /// <param name="width">Target width in pixels.</param>
    /// <param name="height">Target height in pixels.</param>
    /// <returns>The newly allocated GPU <see cref="Texture"/> owned by the caller.</returns>
    public Texture AllocateTargetTexture(uint width, uint height)
    {
        ArgumentOutOfRangeException.ThrowIfZero(width);
        ArgumentOutOfRangeException.ThrowIfZero(height);

        return _system.CreateTexture(new TextureDesc
        {
            Extent = new Extent3D(width, height, 1),
            Format = Format.Rgba8Unorm,
            Usage = TextureUsage.Storage | TextureUsage.Sampled | TextureUsage.TransferSource,
        });
    }

    /// <summary>Uploads geometry into a pooled buffer and records the compute rasterization pass into <paramref name="context"/>.</summary>
    /// <param name="context">Active recording frame context (between scopes).</param>
    /// <param name="data">Flattened SVG geometry and paint payload.</param>
    /// <param name="target">Pre-allocated destination texture created with storage usage.</param>
    public unsafe void RecordRasterize(RenderContext context, in SvgRasterizeData data, Texture target)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentOutOfRangeException.ThrowIfZero(data.Width);
        ArgumentOutOfRangeException.ThrowIfZero(data.Height);

        ulong edgesBytes = (ulong)data.Edges.Count * (ulong)sizeof(Vector4);
        ulong bboxBytes = (ulong)data.ShapeBboxes.Count * (ulong)sizeof(Vector4);
        ulong metaBytes = (ulong)data.ShapeMeta.Count * (ulong)sizeof(uint4);
        ulong colorBytes = (ulong)data.ShapeColors.Count * (ulong)sizeof(Vector4);
        ulong xformBytes = (ulong)data.GradientXforms.Count * (ulong)sizeof(Vector4);
        ulong rampBytes = (ulong)data.GradientRamps.Count * (ulong)sizeof(Vector4);

        const ulong dummyBytes = 16;
        ulong offsetEdges = dummyBytes;
        ulong offsetBbox = offsetEdges + Align16(edgesBytes);
        ulong offsetMeta = offsetBbox + Align16(bboxBytes);
        ulong offsetColors = offsetMeta + Align16(metaBytes);
        ulong offsetXforms = offsetColors + Align16(colorBytes);
        ulong offsetRamps = offsetXforms + Align16(xformBytes);
        ulong totalBytes = offsetRamps + Align16(rampBytes);

        Buffer geometry = AcquireBuffer(context, totalBytes);
        WriteList(geometry, CollectionsMarshal.AsSpan(data.Edges), offsetEdges);
        WriteList(geometry, CollectionsMarshal.AsSpan(data.ShapeBboxes), offsetBbox);
        WriteList(geometry, CollectionsMarshal.AsSpan(data.ShapeMeta), offsetMeta);
        WriteList(geometry, CollectionsMarshal.AsSpan(data.ShapeColors), offsetColors);
        WriteList(geometry, CollectionsMarshal.AsSpan(data.GradientXforms), offsetXforms);
        WriteList(geometry, CollectionsMarshal.AsSpan(data.GradientRamps), offsetRamps);

        _material.SetBuffer("Edges", geometry, data.Edges.Count > 0 ? offsetEdges : 0);
        _material.SetBuffer("ShapeBboxes", geometry, data.ShapeBboxes.Count > 0 ? offsetBbox : 0);
        _material.SetBuffer("ShapeMeta", geometry, data.ShapeMeta.Count > 0 ? offsetMeta : 0);
        _material.SetBuffer("ShapeColors", geometry, data.ShapeColors.Count > 0 ? offsetColors : 0);
        _material.SetBuffer("GradientXforms", geometry, data.GradientXforms.Count > 0 ? offsetXforms : 0);
        _material.SetBuffer("GradientRamps", geometry, data.GradientRamps.Count > 0 ? offsetRamps : 0);
        _material.SetStorageTexture("Output", target);

        uint2 texSize = new(data.Width, data.Height);
        uint shapeCount = (uint)data.ShapeMeta.Count;
        _material.SetData("TexSize", ref texSize);
        _material.SetData("ShapeCount", ref shapeCount);

        Extent3D groups = new((data.Width + 7) / 8, (data.Height + 7) / 8, 1);
        using (RenderContext.ComputeScope compute = context.BeginCompute())
        {
            compute.Dispatch(_material, groups);
        }

        context.Barrier(
            Stage.Compute,
            Access.ShaderWrite,
            Stage.Pixel | Stage.Transfer,
            Access.ShaderRead | Access.TransferRead);
    }

    /// <summary>Allocates a target texture and records its compute rasterization in a single call.</summary>
    /// <param name="context">Active recording frame context (between scopes).</param>
    /// <param name="data">Flattened SVG geometry and paint payload.</param>
    /// <returns>The newly allocated GPU destination texture (contents ready after GPU executes the frame).</returns>
    public Texture Rasterize(RenderContext context, in SvgRasterizeData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        Texture target = AllocateTargetTexture(data.Width, data.Height);
        try
        {
            RecordRasterize(context, in data, target);
            return target;
        }
        catch
        {
            if (!target.IsDisposed)
            {
                target.Dispose();
            }
            throw;
        }
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (!disposing)
            return;

        for (int index = 0; index < _geometryPool.Count; ++index)
        {
            PooledBuffer entry = _geometryPool[index];
            if (entry.Context != null && !entry.Context.IsDisposed && entry.Context.IsFramePending(entry.FrameId))
                entry.Context.Wait();
            if (!entry.Buffer.IsDisposed)
                entry.Buffer.Dispose();
        }
        _geometryPool.Clear();
    }

    private Buffer AcquireBuffer(RenderContext context, ulong requiredBytes)
    {
        for (int index = 0; index < _geometryPool.Count; ++index)
        {
            PooledBuffer entry = _geometryPool[index];
            if (entry.Context != null && !entry.Context.IsDisposed && entry.Context.IsFramePending(entry.FrameId))
                continue;

            if (entry.Buffer.SizeBytes < requiredBytes)
            {
                ulong grown = NextCapacity(Math.Max(requiredBytes, entry.Buffer.SizeBytes * 2));
                entry.Buffer.Dispose();
                entry.Buffer = _system.CreateBuffer(grown);
            }

            entry.Context = context;
            entry.FrameId = context.CurrentFrameId;
            _geometryPool[index] = entry;
            return entry.Buffer;
        }

        Buffer created = _system.CreateBuffer(NextCapacity(requiredBytes));
        _geometryPool.Add(new PooledBuffer(context, context.CurrentFrameId, created));
        return created;
    }

    private static ulong Align16(ulong bytes) => (bytes + 15ul) & ~15ul;

    private static ulong NextCapacity(ulong requiredBytes)
    {
        ulong clamped = Math.Max(requiredBytes, MinGeometryBytes);
        return (clamped + 4095ul) & ~4095ul;
    }

    private static unsafe void WriteList<T>(Buffer buffer, ReadOnlySpan<T> items, ulong byteOffset) where T : unmanaged
    {
        if (!items.IsEmpty)
            buffer.SetData(items, byteOffset);
    }

    private static string LoadEmbeddedShaderSource(string logicalName)
    {
        Assembly assembly = typeof(SvgRenderer).Assembly;
        using Stream stream = assembly.GetManifestResourceStream(logicalName)
            ?? throw new FileNotFoundException($"Missing embedded shader '{logicalName}' in {assembly.FullName}.");
        using StreamReader reader = new(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
