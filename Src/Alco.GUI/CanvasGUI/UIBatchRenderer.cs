using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using Alco.Graphics;
using Alco.Graphics.LowLevel;
using Alco.Rendering;
using Buffer = Alco.Graphics.Buffer;

namespace Alco.GUI;

/// <summary>Bindless quad/text/mesh batcher with dual-mode scissor + stencil clipping and material parameter snapshots.</summary>
internal sealed unsafe class UIBatchRenderer : AutoDisposable
{
    public enum QuadKind : uint
    {
        StraightImage = 0,
        PremultipliedImage = 1,
        DistanceGlyph = 2,
        BitmapGlyph = 3,
        StraightTiledImage = 4,
        PremultipliedTiledImage = 5,
        StraightNearestImage = 6,
        PremultipliedNearestImage = 7,
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct UIQuad
    {
        public Vector2 Origin;
        public Vector2 Right;
        public Vector2 Down;
        public uint TextureIndex;
        public QuadKind Kind;
        public Vector4 UvRect;
        public Vector4 Color;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct UIPassHeader
    {
        public GPUBufferView Quads;
        public Vector2 ClipScale;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct UIMeshRoot
    {
        public GPUBufferView Vertices;
        public Vector2 ClipScale;
        public uint TextureIndex;
        public QuadKind Kind;
    }

    private enum DrawOp : byte
    {
        Content,
        Push,
        Pop,
        Mesh,
    }

    private struct DrawRun
    {
        public DrawOp Op;
        public int First;
        public int Count;
        public int IndexFirst;
        public int IndexCount;
        public int ScissorIndex;
        public int FallbackScissorIndex;
        public byte StencilDepth;
        public uint MeshTextureIndex;
        public QuadKind MeshKind;
        public Material? Material;
        public int RootSize;
        public int SnapshotOffset;
    }

    private struct ClipRect(float minX, float minY, float maxX, float maxY)
    {
        public float MinX = minX;
        public float MinY = minY;
        public float MaxX = maxX;
        public float MaxY = maxY;

        public readonly bool IsEmpty => MinX >= MaxX || MinY >= MaxY;

        public readonly bool EqualsRect(in ClipRect other) =>
            MathF.Abs(MinX - other.MinX) <= 1e-4f &&
            MathF.Abs(MinY - other.MinY) <= 1e-4f &&
            MathF.Abs(MaxX - other.MaxX) <= 1e-4f &&
            MathF.Abs(MaxY - other.MaxY) <= 1e-4f;

        public static ClipRect Intersect(in ClipRect a, in ClipRect b) =>
            new(MathF.Max(a.MinX, b.MinX), MathF.Max(a.MinY, b.MinY), MathF.Min(a.MaxX, b.MaxX), MathF.Min(a.MaxY, b.MaxY));
    }

    private struct MaskEntry
    {
        public bool IsScissorOnly;
        public bool ClippedEmptyIncremented;
        public bool FallbackEmptyIncremented;
        public int PreviousScissorIndex;
        public int PreviousFallbackScissorIndex;
        public bool StencilFlushed;
        public int QuadIndex;
        public byte StencilDepthBefore;
        public Matrix4x4 Matrix;
        public Texture? Texture;
        public Rect UvRect;
    }

    private struct PooledBuffer(RenderContext? context, ulong frameId, Buffer buffer)
    {
        public RenderContext? Context = context;
        public ulong FrameId = frameId;
        public Buffer Buffer = buffer;
    }

    public static readonly BlendState PremultiplyBlend = new()
    {
        Enabled = true,
        Color = new BlendComponentState
        {
            Source = BlendFactor.One,
            Destination = BlendFactor.OneMinusSourceAlpha,
            Operation = BlendOp.Add,
        },
        Alpha = new BlendComponentState
        {
            Source = BlendFactor.One,
            Destination = BlendFactor.OneMinusSourceAlpha,
            Operation = BlendOp.Add,
        },
    };

    private const ulong MinQuadBufferBytes = 64 * 1024;
    private const ulong MinMeshVertexBufferBytes = 16 * 1024;
    private const ulong MinMeshIndexBufferBytes = 8 * 1024;

    private readonly RenderingSystem _system;
    private readonly GlyphCache _glyphCache;
    private readonly Material _defaultMaterial;
    private readonly Material _maskMaterial;
    private readonly Material _meshMaterial;

    private readonly List<UIQuad> _quads = new(512);
    private readonly List<UIMeshVertex> _meshVertices = new(256);
    private readonly List<ushort> _meshIndices = new(512);
    private readonly List<DrawRun> _runs = new(64);
    private readonly List<ClipRect> _scissorRects = new(16);
    private readonly List<MaskEntry> _maskStack = new(16);
    private readonly HashSet<Texture> _usedTextures = new(32);

    private readonly List<PooledBuffer> _quadPool = new(4);
    private readonly List<PooledBuffer> _meshVertexPool = new(4);
    private readonly List<PooledBuffer> _meshIndexPool = new(4);

    private byte[] _materialSnapshotArena = new byte[1024];
    private int _materialSnapshotCount;
    private byte[] _tempRootScratch = new byte[256];

    private Vector2 _canvasSize;
    private int _currentScissorIndex;
    private int _currentFallbackScissorIndex;
    private int _clipEmptyDepth;
    private int _fallbackEmptyDepth;
    private byte _stencilDepth;
    private int _contentQuads;
    private int _scissorMaskCount;
    private int _stencilMaskCount;
    private int _elidedMaskCount;
    private bool _hasAnyStencilMask;
    private bool _warnedNoStencil;

    private Buffer? _activeQuadBuffer;
    private Buffer? _activeMeshVertexBuffer;
    private Buffer? _activeMeshIndexBuffer;
    private int _glyphCountAtBegin;
    private int _lastFontGlyphsUploaded;
    private int _lastFontPageCount;
    private long _lastFontAtlasBytes;
    private UIFrameStats _lastStats;

    public bool SplitDraws { get; set; }

    public UIFrameStats LastStats => _lastStats;

    public UIBatchRenderer(RenderingSystem system, GlyphCache glyphCache)
    {
        _system = system ?? throw new ArgumentNullException(nameof(system));
        _glyphCache = glyphCache ?? throw new ArgumentNullException(nameof(glyphCache));

        Shader defaultShader = system.Shaders.GetShaderFromSource("AlcoGUI_Default", "AlcoGUI_Default.slang",
            AlcoGUIShaders.LoadEmbeddedSource("AlcoGUI_Default.slang"), ShaderKind.Graphics);
        Shader maskShader = system.Shaders.GetShaderFromSource("AlcoGUI_Mask", "AlcoGUI_Mask.slang",
            AlcoGUIShaders.LoadEmbeddedSource("AlcoGUI_Mask.slang"), ShaderKind.Graphics);
        Shader meshShader = system.Shaders.GetShaderFromSource("AlcoGUI_Mesh", "AlcoGUI_Mesh.slang",
            AlcoGUIShaders.LoadEmbeddedSource("AlcoGUI_Mesh.slang"), ShaderKind.Graphics);

        RasterizationState noCull = new() { Cull = CullMode.None };

        _defaultMaterial = defaultShader.CreateMaterial("AlcoGUI_Default");
        _defaultMaterial.Blend = PremultiplyBlend;
        _defaultMaterial.Rasterization = noCull;

        _maskMaterial = maskShader.CreateMaterial("AlcoGUI_Mask");
        _maskMaterial.WriteMask = 0;
        _maskMaterial.Rasterization = noCull;

        _meshMaterial = meshShader.CreateMaterial("AlcoGUI_Mesh");
        _meshMaterial.Blend = PremultiplyBlend;
        _meshMaterial.Rasterization = noCull;
    }

    public void Begin(Vector2 canvasSize)
    {
        _canvasSize = new Vector2(MathF.Max(1f, canvasSize.X), MathF.Max(1f, canvasSize.Y));
        _quads.Clear();
        _meshVertices.Clear();
        _meshIndices.Clear();
        _runs.Clear();
        _scissorRects.Clear();
        _maskStack.Clear();
        _usedTextures.Clear();
        _materialSnapshotCount = 0;

        float halfW = _canvasSize.X * 0.5f;
        float halfH = _canvasSize.Y * 0.5f;
        _scissorRects.Add(new ClipRect(-halfW, -halfH, halfW, halfH));
        _currentScissorIndex = 0;
        _currentFallbackScissorIndex = 0;
        _clipEmptyDepth = 0;
        _fallbackEmptyDepth = 0;
        _stencilDepth = 0;
        _contentQuads = 0;
        _scissorMaskCount = 0;
        _stencilMaskCount = 0;
        _elidedMaskCount = 0;
        _hasAnyStencilMask = false;

        _activeQuadBuffer = null;
        _activeMeshVertexBuffer = null;
        _activeMeshIndexBuffer = null;
        _glyphCountAtBegin = _glyphCache.GeneratedGlyphCount;
        _lastFontGlyphsUploaded = 0;
        _lastFontPageCount = _glyphCache.PageCount;
        _lastFontAtlasBytes = _glyphCache.AtlasBytes;
    }

    public void PushMask(Matrix4x4 matrix, Texture? texture, Rect uvRect)
    {
        if (_clipEmptyDepth > 0)
        {
            ++_clipEmptyDepth;
            _maskStack.Add(new MaskEntry
            {
                ClippedEmptyIncremented = true,
                PreviousScissorIndex = _currentScissorIndex,
                PreviousFallbackScissorIndex = _currentFallbackScissorIndex,
            });
            return;
        }

        ExtractQuadAxes(matrix, out Vector2 origin, out Vector2 right, out Vector2 down);
        if ((right.LengthSquared() <= 1e-8f) || (down.LengthSquared() <= 1e-8f))
        {
            ++_clipEmptyDepth;
            _maskStack.Add(new MaskEntry
            {
                ClippedEmptyIncremented = true,
                PreviousScissorIndex = _currentScissorIndex,
                PreviousFallbackScissorIndex = _currentFallbackScissorIndex,
            });
            return;
        }

        Vector2 center = new(matrix.M41, matrix.M42);
        Vector2 axisX = new(matrix.M11, matrix.M12);
        Vector2 axisY = new(matrix.M21, matrix.M22);
        bool axisAligned = MathF.Abs(axisX.Y) <= 1e-4f && MathF.Abs(axisY.X) <= 1e-4f;

        if (texture == null && axisAligned)
        {
            float halfW = MathF.Abs(axisX.X) * 0.5f;
            float halfH = MathF.Abs(axisY.Y) * 0.5f;
            ClipRect local = new(center.X - halfW, center.Y - halfH, center.X + halfW, center.Y + halfH);
            ClipRect nextScissor = ClipRect.Intersect(_scissorRects[_currentScissorIndex], local);
            if (nextScissor.IsEmpty)
            {
                ++_clipEmptyDepth;
                _maskStack.Add(new MaskEntry
                {
                    IsScissorOnly = true,
                    ClippedEmptyIncremented = true,
                    PreviousScissorIndex = _currentScissorIndex,
                    PreviousFallbackScissorIndex = _currentFallbackScissorIndex,
                });
                return;
            }

            ++_scissorMaskCount;
            int nextIndex = GetOrAddScissorRect(in nextScissor, _currentScissorIndex);
            int nextFallback = _fallbackEmptyDepth == 0
                ? GetOrAddScissorRect(ClipRect.Intersect(_scissorRects[_currentFallbackScissorIndex], local), _currentFallbackScissorIndex)
                : _currentFallbackScissorIndex;

            _maskStack.Add(new MaskEntry
            {
                IsScissorOnly = true,
                PreviousScissorIndex = _currentScissorIndex,
                PreviousFallbackScissorIndex = _currentFallbackScissorIndex,
            });
            _currentScissorIndex = nextIndex;
            _currentFallbackScissorIndex = nextFallback;
            return;
        }

        // Stencil path (rotated or textured mask) with lazy push + rotated AABB fallback.
        float aabbHalfW = (MathF.Abs(axisX.X) + MathF.Abs(axisY.X)) * 0.5f;
        float aabbHalfH = (MathF.Abs(axisX.Y) + MathF.Abs(axisY.Y)) * 0.5f;
        ClipRect aabb = new(center.X - aabbHalfW, center.Y - aabbHalfH, center.X + aabbHalfW, center.Y + aabbHalfH);
        int prevFallback = _currentFallbackScissorIndex;
        bool fallbackEmptyInc = false;
        if (_fallbackEmptyDepth > 0)
        {
            ++_fallbackEmptyDepth;
            fallbackEmptyInc = true;
        }
        else
        {
            ClipRect intersected = ClipRect.Intersect(_scissorRects[_currentFallbackScissorIndex], aabb);
            if (intersected.IsEmpty)
            {
                ++_fallbackEmptyDepth;
                fallbackEmptyInc = true;
            }
            else
            {
                _currentFallbackScissorIndex = GetOrAddScissorRect(in intersected, _currentFallbackScissorIndex);
            }
        }

        _maskStack.Add(new MaskEntry
        {
            IsScissorOnly = false,
            FallbackEmptyIncremented = fallbackEmptyInc,
            PreviousScissorIndex = _currentScissorIndex,
            PreviousFallbackScissorIndex = prevFallback,
            StencilDepthBefore = _stencilDepth,
            Matrix = matrix,
            Texture = texture,
            UvRect = uvRect,
        });
    }

    public void PopMask()
    {
        if (_maskStack.Count == 0)
            return;

        int lastIdx = _maskStack.Count - 1;
        MaskEntry entry = _maskStack[lastIdx];
        _maskStack.RemoveAt(lastIdx);

        if (entry.ClippedEmptyIncremented)
            --_clipEmptyDepth;
        if (entry.FallbackEmptyIncremented)
            --_fallbackEmptyDepth;

        _currentScissorIndex = entry.PreviousScissorIndex;
        _currentFallbackScissorIndex = entry.PreviousFallbackScissorIndex;

        if (!entry.IsScissorOnly && !entry.ClippedEmptyIncremented)
        {
            if (entry.StencilFlushed)
            {
                _runs.Add(new DrawRun
                {
                    Op = DrawOp.Pop,
                    First = entry.QuadIndex,
                    Count = 1,
                    ScissorIndex = entry.PreviousScissorIndex,
                    FallbackScissorIndex = -1,
                    StencilDepth = _stencilDepth,
                });
                --_stencilDepth;
            }
            else
            {
                ++_elidedMaskCount;
            }
        }
    }

    public void DrawQuad(
        Matrix4x4 matrix,
        Texture? texture,
        Rect uvRect,
        ColorFloat color,
        QuadKind kind,
        Material? material = null)
    {
        if (_clipEmptyDepth > 0 || color.A <= 0f)
            return;

        ExtractQuadAxes(matrix, out Vector2 origin, out Vector2 right, out Vector2 down);
        if (right.LengthSquared() <= 1e-10f && down.LengthSquared() <= 1e-10f)
            return;

        EmitQuadInternal(origin, right, down, texture, uvRect, color, kind, material);
    }

    public void DrawNineSlice(
        Matrix4x4 matrix,
        Texture texture,
        Rect uvRect,
        ColorFloat color,
        Vector4 sliceBorders,
        bool premultiplied,
        bool nearestFilter,
        Material? material = null)
    {
        if (_clipEmptyDepth > 0 || color.A <= 0f)
            return;

        Vector2 axisX = new(matrix.M11, matrix.M12);
        Vector2 axisY = new(matrix.M21, matrix.M22);
        float targetWidth = axisX.Length();
        float targetHeight = axisY.Length();
        if (targetWidth <= 1e-5f || targetHeight <= 1e-5f)
            return;

        float sourceWidth = MathF.Abs(uvRect.Size.X) * texture.Desc.Extent.X;
        float sourceHeight = MathF.Abs(uvRect.Size.Y) * texture.Desc.Extent.Y;
        if (sourceWidth <= 0f || sourceHeight <= 0f)
        {
            DrawQuad(matrix, texture, uvRect, color,
                ResolveImageKind(premultiplied, tiled: false, nearestFilter), material);
            return;
        }

        Vector4 srcX = SliceCuts(sourceWidth, Math.Clamp(sliceBorders.X, 0f, sourceWidth), Math.Clamp(sliceBorders.Z, 0f, sourceWidth));
        Vector4 srcY = SliceCuts(sourceHeight, Math.Clamp(sliceBorders.Y, 0f, sourceHeight), Math.Clamp(sliceBorders.W, 0f, sourceHeight));
        Vector4 dstX = SliceCuts(targetWidth, srcX.Y, srcX.W - srcX.Z);
        Vector4 dstY = SliceCuts(targetHeight, srcY.Y, srcY.W - srcY.Z);

        Vector2 unitRight = axisX / targetWidth;
        Vector2 unitDown = -axisY / targetHeight;
        Vector2 center = new(matrix.M41, matrix.M42);
        Vector2 topLeft = center - axisX * 0.5f + axisY * 0.5f;

        float uScale = uvRect.Size.X / sourceWidth;
        float vScale = uvRect.Size.Y / sourceHeight;
        QuadKind kind = ResolveImageKind(premultiplied, tiled: false, nearestFilter);

        for (int y = 0; y < 3; ++y)
        {
            float patchHeight = dstY[y + 1] - dstY[y];
            if (patchHeight <= 1e-5f)
                continue;

            float v0 = uvRect.Origin.Y + srcY[y] * vScale;
            float dv = (srcY[y + 1] - srcY[y]) * vScale;
            for (int x = 0; x < 3; ++x)
            {
                float patchWidth = dstX[x + 1] - dstX[x];
                if (patchWidth <= 1e-5f)
                    continue;

                Rect patchUv = new(uvRect.Origin.X + srcX[x] * uScale, v0, (srcX[x + 1] - srcX[x]) * uScale, dv);
                Vector2 patchOrigin = topLeft + unitRight * dstX[x] + unitDown * dstY[y];
                EmitQuadInternal(patchOrigin, unitRight * patchWidth, unitDown * patchHeight, texture, patchUv, color, kind, material);
            }
        }
    }

    public float DrawChars(
        Font font,
        ReadOnlySpan<char> chars,
        Matrix4x4 matrix,
        Pivot pivot,
        ColorFloat color,
        ReadOnlySpan<TextSlice> slices,
        float lineSpacing,
        Material? material = null)
    {
        ArgumentNullException.ThrowIfNull(font);
        if (chars.IsEmpty)
            return 0f;

        if (_clipEmptyDepth > 0 || color.A <= 0f)
            return MeasureMaxLineWidth(font, chars);

        Vector2 axisX = new(matrix.M11, matrix.M12);
        Vector2 axisY = new(matrix.M21, matrix.M22);
        Vector2 originPos = new(matrix.M41, matrix.M42);

        int lineCount = 1;
        for (int i = 0; i < chars.Length; ++i)
        {
            if (chars[i] == '\n')
                ++lineCount;
        }

        float totalHeight = font.Ascent + font.Descent;
        float baselineY = (0.5f - pivot.value.Y) * totalHeight - font.Ascent +
            (lineCount - 1) * lineSpacing * (0.5f - pivot.value.Y);

        float maxAdvance = 0f;
        int lineStart = 0;
        int sliceCursor = 0;

        for (int i = 0; i <= chars.Length; ++i)
        {
            bool atEnd = i == chars.Length;
            if (!atEnd && chars[i] != '\n')
                continue;

            int lineEnd = i;
            if (lineEnd > lineStart && chars[lineEnd - 1] == '\r')
                --lineEnd;

            ReadOnlySpan<char> lineSpan = chars[lineStart..lineEnd];
            float lineWidth = font.GetNormalizedTextWidth(lineSpan);
            maxAdvance = MathF.Max(maxAdvance, lineWidth);

            float penX = -(0.5f + pivot.value.X) * lineWidth;
            int offsetInChars = 0;
            while (offsetInChars < lineSpan.Length)
            {
                int absCharIndex = lineStart + offsetInChars;
                if (Rune.DecodeFromUtf16(lineSpan[offsetInChars..], out Rune rune, out int consumed) != System.Buffers.OperationStatus.Done)
                {
                    rune = Rune.ReplacementChar;
                    consumed = Math.Max(1, consumed);
                }

                ColorFloat charColor = ResolveSliceColor(absCharIndex, color, slices, ref sliceCursor);
                GlyphImage glyph = _glyphCache.ResolveGlyph(font, rune);
                if (glyph.HasVisual && charColor.A > 0f && glyph.Page != null)
                {
                    Vector2 emTopLeft = new(penX + glyph.Offset.X, baselineY + glyph.Offset.Y);
                    Vector2 worldOrigin = originPos + axisX * emTopLeft.X + axisY * emTopLeft.Y;
                    Vector2 right = axisX * glyph.Size.X;
                    Vector2 down = -axisY * glyph.Size.Y;
                    Rect uv = new(glyph.UvRect.X, glyph.UvRect.Y, glyph.UvRect.Z, glyph.UvRect.W);
                    EmitQuadInternal(worldOrigin, right, down, glyph.Page, uv, charColor,
                        glyph.IsDistanceField ? QuadKind.DistanceGlyph : QuadKind.BitmapGlyph, material);
                }

                penX += glyph.Advance;
                offsetInChars += consumed;
            }

            baselineY -= lineSpacing;
            lineStart = i + 1;
        }

        return maxAdvance;
    }

    public void DrawMesh(
        ReadOnlySpan<UIMeshVertex> vertices,
        ReadOnlySpan<ushort> indices,
        Texture? texture,
        QuadKind kind)
    {
        if (_clipEmptyDepth > 0 || vertices.IsEmpty || indices.IsEmpty)
            return;

        if (vertices.Length > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(vertices), "UI mesh draw exceeds 65535 vertices.");

        FlushPendingStencilMasks();

        int firstVertex = _meshVertices.Count;
        for (int i = 0; i < vertices.Length; ++i)
            _meshVertices.Add(vertices[i]);

        int firstIndex = _meshIndices.Count;
        for (int i = 0; i < indices.Length; ++i)
            _meshIndices.Add(indices[i]);

        Texture resolvedTexture = texture ?? _system.TextureWhite;
        _usedTextures.Add(resolvedTexture);

        _runs.Add(new DrawRun
        {
            Op = DrawOp.Mesh,
            First = firstVertex,
            Count = vertices.Length,
            IndexFirst = firstIndex,
            IndexCount = indices.Length,
            ScissorIndex = _currentScissorIndex,
            FallbackScissorIndex = _fallbackEmptyDepth > 0 ? -1 : _currentFallbackScissorIndex,
            StencilDepth = _stencilDepth,
            MeshTextureIndex = resolvedTexture.SampledIndex,
            MeshKind = kind,
        });
    }

    public void EndCollection()
    {
        while (_maskStack.Count > 0)
            PopMask();
    }

    public void Upload(RenderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        EndCollection();

        _lastFontGlyphsUploaded = _glyphCache.GeneratedGlyphCount - _glyphCountAtBegin;
        _lastFontPageCount = _glyphCache.PageCount;
        _lastFontAtlasBytes = _glyphCache.AtlasBytes;

        foreach (Texture tex in _usedTextures)
        {
            if (!tex.IsDestroyed)
                context.Retain(tex);
        }

        if (_quads.Count > 0)
        {
            ulong quadBytes = checked((ulong)_quads.Count * (ulong)sizeof(UIQuad));
            _activeQuadBuffer = AcquireTransientBuffer(_quadPool, context, quadBytes, MinQuadBufferBytes);
            _activeQuadBuffer.SetData(CollectionsMarshal.AsSpan(_quads));
            context.Retain(_activeQuadBuffer);
        }

        if (_meshVertices.Count > 0 && _meshIndices.Count > 0)
        {
            ulong vtxBytes = checked((ulong)_meshVertices.Count * (ulong)sizeof(UIMeshVertex));
            ulong idxBytes = checked((ulong)_meshIndices.Count * sizeof(ushort));
            _activeMeshVertexBuffer = AcquireTransientBuffer(_meshVertexPool, context, vtxBytes, MinMeshVertexBufferBytes);
            _activeMeshIndexBuffer = AcquireTransientBuffer(_meshIndexPool, context, idxBytes, MinMeshIndexBufferBytes);
            _activeMeshVertexBuffer.SetData(CollectionsMarshal.AsSpan(_meshVertices));
            _activeMeshIndexBuffer.SetData(CollectionsMarshal.AsSpan(_meshIndices));
            context.Retain(_activeMeshVertexBuffer);
            context.Retain(_activeMeshIndexBuffer);
        }
    }

    public UIFrameStats Record(RenderContext context, in RenderContext.RenderScope render, Extent2D viewportExtent, bool hasStencil)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (_runs.Count == 0 || viewportExtent.X == 0 || viewportExtent.Y == 0)
        {
            _lastStats = new UIFrameStats(
                _contentQuads, 0, 0, 0, 0, _stencilMaskCount, _scissorMaskCount, _elidedMaskCount,
                _lastFontGlyphsUploaded, _lastFontPageCount, _lastFontAtlasBytes, SplitDraws);
            return _lastStats;
        }

        if (_hasAnyStencilMask && !hasStencil && !_warnedNoStencil)
        {
            _warnedNoStencil = true;
            Log.Warning("Canvas rendered stencil UIMask nodes without a stencil attachment; falling back to AABB scissor clipping.");
        }

        render.SetViewport(new Viewport { Width = viewportExtent.X, Height = viewportExtent.Y, MaxDepth = 1f });

        Vector2 clipScale = new(2.0f / _canvasSize.X, 2.0f / _canvasSize.Y);
        int contentDraws = 0;
        int maskDraws = 0;
        int meshDraws = 0;
        int scissorChanges = 0;
        Scissor? lastScissor = null;

        ReadOnlySpan<DrawRun> runs = CollectionsMarshal.AsSpan(_runs);
        for (int i = 0; i < runs.Length; ++i)
        {
            ref readonly DrawRun run = ref runs[i];
            bool isMaskOp = run.Op is DrawOp.Push or DrawOp.Pop;
            if (!hasStencil && isMaskOp)
                continue;

            int scissorIdx = hasStencil ? run.ScissorIndex : run.FallbackScissorIndex;
            if (scissorIdx < 0)
                continue;

            if (!TryComputePixelScissor(_scissorRects[scissorIdx], viewportExtent, out Scissor scissor))
                continue;

            if (lastScissor != scissor)
            {
                render.SetScissor(scissor);
                lastScissor = scissor;
                ++scissorChanges;
            }

            if (run.Op == DrawOp.Mesh)
            {
                Debug.Assert(_activeMeshVertexBuffer != null && _activeMeshIndexBuffer != null);
                DepthStencilState ds = BuildDepthStencilState(hasStencil, DrawOp.Content, run.StencilDepth);
                GPUAllocation<UIMeshRoot> meshRoot = context.FrameData.Allocate<UIMeshRoot>();
                *meshRoot.Cpu = new UIMeshRoot
                {
                    Vertices = new GPUBufferView
                    {
                        Index = _activeMeshVertexBuffer.View.Index,
                        Offset = checked((uint)(_activeMeshVertexBuffer.View.Offset + (ulong)run.First * (ulong)sizeof(UIMeshVertex))),
                    },
                    ClipScale = clipScale,
                    TextureIndex = run.MeshTextureIndex,
                    Kind = run.MeshKind,
                };
                render.DrawIndexed(
                    _meshMaterial,
                    meshRoot.GPURange,
                    ds,
                    _activeMeshIndexBuffer,
                    IndexType.UInt16,
                    (uint)run.IndexCount,
                    byteOffset: (ulong)run.IndexFirst * sizeof(ushort));
                ++meshDraws;
                continue;
            }

            Debug.Assert(_activeQuadBuffer != null);
            Material drawMaterial = isMaskOp ? _maskMaterial : (run.Material ?? _defaultMaterial);
            DepthStencilState depthStencil = BuildDepthStencilState(hasStencil, run.Op, run.StencilDepth);

            GPUBufferView quadSlice = new()
            {
                Index = _activeQuadBuffer.View.Index,
                Offset = checked((uint)(_activeQuadBuffer.View.Offset + (ulong)run.First * (ulong)sizeof(UIQuad))),
            };
            UIPassHeader header = new() { Quads = quadSlice, ClipScale = clipScale };

            GPURange rootRange;
            if (run.Material == null || isMaskOp)
            {
                GPUAllocation<UIPassHeader> headerAlloc = context.FrameData.Allocate<UIPassHeader>();
                *headerAlloc.Cpu = header;
                rootRange = headerAlloc.GPURange;
            }
            else
            {
                GPUAllocation<byte> customAlloc = context.FrameData.Allocate((ulong)run.RootSize);
                Span<byte> dest = customAlloc.CpuSpan;
                MemoryMarshal.Write(dest, in header);
                int tailLen = run.RootSize - sizeof(UIPassHeader);
                if (tailLen > 0)
                    _materialSnapshotArena.AsSpan(run.SnapshotOffset, tailLen).CopyTo(dest.Slice(sizeof(UIPassHeader), tailLen));
                rootRange = customAlloc.GPURange;
            }

            render.Draw(drawMaterial, rootRange, depthStencil, 6, (uint)run.Count, firstVertex: 0, firstInstance: 0);
            if (isMaskOp)
                ++maskDraws;
            else
                ++contentDraws;
        }

        _lastStats = new UIFrameStats(
            _contentQuads, contentDraws, maskDraws, meshDraws, scissorChanges,
            _stencilMaskCount, _scissorMaskCount, _elidedMaskCount,
            _lastFontGlyphsUploaded, _lastFontPageCount, _lastFontAtlasBytes, SplitDraws);
        return _lastStats;
    }

    public static QuadKind ResolveImageKind(bool premultiplied, bool tiled, bool nearestFilter)
    {
        if (tiled)
            return premultiplied ? QuadKind.PremultipliedTiledImage : QuadKind.StraightTiledImage;
        if (nearestFilter)
            return premultiplied ? QuadKind.PremultipliedNearestImage : QuadKind.StraightNearestImage;
        return premultiplied ? QuadKind.PremultipliedImage : QuadKind.StraightImage;
    }

    protected override void Dispose(bool disposing)
    {
        if (!disposing)
            return;

        DisposePool(_quadPool);
        DisposePool(_meshVertexPool);
        DisposePool(_meshIndexPool);
        _usedTextures.Clear();
    }

    private void EmitQuadInternal(
        Vector2 origin,
        Vector2 right,
        Vector2 down,
        Texture? texture,
        Rect uvRect,
        ColorFloat color,
        QuadKind kind,
        Material? material)
    {
        FlushPendingStencilMasks();

        int tailLen = 0;
        int rootSize = sizeof(UIPassHeader);
        if (material != null)
        {
            if (material.Shader.Kind != ShaderKind.Graphics || material.Root.SizeBytes < (uint)sizeof(UIPassHeader))
            {
                throw new ArgumentException(
                    $"UI custom material '{material.Name}' must be graphics with root size >= 16 bytes (starting with UIPassHeader).",
                    nameof(material));
            }
            rootSize = (int)material.Root.SizeBytes;
            tailLen = rootSize - sizeof(UIPassHeader);
            if (_tempRootScratch.Length < rootSize)
                _tempRootScratch = new byte[Math.Max(rootSize, _tempRootScratch.Length * 2)];
            material.CopyRoot(_tempRootScratch.AsSpan(0, rootSize));
        }

        Texture resolvedTexture = texture ?? _system.TextureWhite;
        _usedTextures.Add(resolvedTexture);

        int quadIndex = _quads.Count;
        _quads.Add(new UIQuad
        {
            Origin = origin,
            Right = right,
            Down = down,
            TextureIndex = resolvedTexture.SampledIndex,
            Kind = kind,
            UvRect = new Vector4(uvRect.Origin.X, uvRect.Origin.Y, uvRect.Size.X, uvRect.Size.Y),
            Color = new Vector4(color.R, color.G, color.B, color.A),
        });
        ++_contentQuads;

        ReadOnlySpan<byte> tailBytes = tailLen > 0 ? _tempRootScratch.AsSpan(sizeof(UIPassHeader), tailLen) : default;
        if (!SplitDraws && _runs.Count > 0)
        {
            ref DrawRun prev = ref CollectionsMarshal.AsSpan(_runs)[^1];
            if (prev.Op == DrawOp.Content &&
                prev.ScissorIndex == _currentScissorIndex &&
                prev.FallbackScissorIndex == (_fallbackEmptyDepth > 0 ? -1 : _currentFallbackScissorIndex) &&
                prev.StencilDepth == _stencilDepth &&
                ReferenceEquals(prev.Material, material) &&
                prev.RootSize == rootSize &&
                (tailLen == 0 || _materialSnapshotArena.AsSpan(prev.SnapshotOffset, tailLen).SequenceEqual(tailBytes)))
            {
                ++prev.Count;
                return;
            }
        }

        int snapshotOffset = 0;
        if (tailLen > 0)
        {
            snapshotOffset = AppendMaterialSnapshot(tailBytes);
        }

        _runs.Add(new DrawRun
        {
            Op = DrawOp.Content,
            First = quadIndex,
            Count = 1,
            ScissorIndex = _currentScissorIndex,
            FallbackScissorIndex = _fallbackEmptyDepth > 0 ? -1 : _currentFallbackScissorIndex,
            StencilDepth = _stencilDepth,
            Material = material,
            RootSize = rootSize,
            SnapshotOffset = snapshotOffset,
        });
    }

    private void FlushPendingStencilMasks()
    {
        for (int i = 0; i < _maskStack.Count; ++i)
        {
            MaskEntry entry = _maskStack[i];
            if (entry.IsScissorOnly || entry.ClippedEmptyIncremented || entry.StencilFlushed)
                continue;

            if (_stencilDepth == byte.MaxValue)
                throw new InvalidOperationException("UI stencil masks are nested more than 255 levels deep.");

            ExtractQuadAxes(entry.Matrix, out Vector2 origin, out Vector2 right, out Vector2 down);
            Texture resolvedTexture = entry.Texture ?? _system.TextureWhite;
            if (entry.Texture != null)
                _usedTextures.Add(resolvedTexture);

            int quadIndex = _quads.Count;
            _quads.Add(new UIQuad
            {
                Origin = origin,
                Right = right,
                Down = down,
                TextureIndex = entry.Texture != null ? resolvedTexture.SampledIndex : 0u,
                Kind = QuadKind.StraightImage,
                UvRect = new Vector4(entry.UvRect.Origin.X, entry.UvRect.Origin.Y, entry.UvRect.Size.X, entry.UvRect.Size.Y),
                Color = Vector4.One,
            });

            entry.StencilFlushed = true;
            entry.QuadIndex = quadIndex;
            entry.StencilDepthBefore = _stencilDepth;
            _maskStack[i] = entry;

            _runs.Add(new DrawRun
            {
                Op = DrawOp.Push,
                First = quadIndex,
                Count = 1,
                ScissorIndex = entry.PreviousScissorIndex,
                FallbackScissorIndex = -1,
                StencilDepth = _stencilDepth,
            });
            ++_stencilDepth;
            ++_stencilMaskCount;
            _hasAnyStencilMask = true;
        }
    }

    private int AppendMaterialSnapshot(ReadOnlySpan<byte> bytes)
    {
        int offset = _materialSnapshotCount;
        int required = offset + bytes.Length;
        if (required > _materialSnapshotArena.Length)
        {
            int nextSize = Math.Max(required, _materialSnapshotArena.Length * 2);
            Array.Resize(ref _materialSnapshotArena, nextSize);
        }
        bytes.CopyTo(_materialSnapshotArena.AsSpan(offset, bytes.Length));
        _materialSnapshotCount = required;
        return offset;
    }

    private int GetOrAddScissorRect(in ClipRect rect, int parentIndex)
    {
        if (rect.EqualsRect(_scissorRects[parentIndex]))
            return parentIndex;
        int idx = _scissorRects.Count;
        _scissorRects.Add(rect);
        return idx;
    }

    private bool TryComputePixelScissor(in ClipRect rect, Extent2D viewportExtent, out Scissor scissor)
    {
        float nx0 = rect.MinX / _canvasSize.X + 0.5f;
        float nx1 = rect.MaxX / _canvasSize.X + 0.5f;
        float ny0 = 0.5f - rect.MaxY / _canvasSize.Y;
        float ny1 = 0.5f - rect.MinY / _canvasSize.Y;

        int x0 = Math.Clamp((int)MathF.Floor(nx0 * viewportExtent.X), 0, (int)viewportExtent.X);
        int x1 = Math.Clamp((int)MathF.Ceiling(nx1 * viewportExtent.X), x0, (int)viewportExtent.X);
        int y0 = Math.Clamp((int)MathF.Floor(ny0 * viewportExtent.Y), 0, (int)viewportExtent.Y);
        int y1 = Math.Clamp((int)MathF.Ceiling(ny1 * viewportExtent.Y), y0, (int)viewportExtent.Y);

        if (x1 <= x0 || y1 <= y0)
        {
            scissor = default;
            return false;
        }

        scissor = new Scissor
        {
            X = x0,
            Y = y0,
            Width = (uint)(x1 - x0),
            Height = (uint)(y1 - y0),
        };
        return true;
    }

    private static DepthStencilState BuildDepthStencilState(bool hasStencil, DrawOp op, byte stencilRef)
    {
        if (!hasStencil || (op == DrawOp.Content && stencilRef == 0))
            return new DepthStencilState();

        StencilOp passOp = op switch
        {
            DrawOp.Push => StencilOp.IncrementClamp,
            DrawOp.Pop => StencilOp.DecrementClamp,
            _ => StencilOp.Keep,
        };
        StencilFaceState face = new()
        {
            Compare = CompareOp.Equal,
            Pass = passOp,
            Fail = StencilOp.Keep,
            DepthFail = StencilOp.Keep,
            Reference = stencilRef,
        };
        return new DepthStencilState
        {
            StencilTest = true,
            StencilReadMask = byte.MaxValue,
            StencilWriteMask = op == DrawOp.Content ? (byte)0 : byte.MaxValue,
            Front = face,
            Back = face,
        };
    }

    private Buffer AcquireTransientBuffer(List<PooledBuffer> pool, RenderContext context, ulong requiredBytes, ulong minBytes)
    {
        for (int i = 0; i < pool.Count; ++i)
        {
            PooledBuffer entry = pool[i];
            if (entry.Context != null && !entry.Context.IsDestroyed && entry.Context.IsFramePending(entry.FrameId))
                continue;

            if (entry.Buffer.SizeBytes < requiredBytes)
            {
                ulong grown = NextCapacity(Math.Max(requiredBytes, Math.Max(minBytes, entry.Buffer.SizeBytes * 2)));
                entry.Buffer.Destroy();
                entry.Buffer = _system.CreateBuffer(grown);
            }

            entry.Context = context;
            entry.FrameId = context.CurrentFrameId;
            pool[i] = entry;
            return entry.Buffer;
        }

        Buffer created = _system.CreateBuffer(NextCapacity(Math.Max(requiredBytes, minBytes)));
        pool.Add(new PooledBuffer(context, context.CurrentFrameId, created));
        return created;
    }

    private static void DisposePool(List<PooledBuffer> pool)
    {
        for (int i = 0; i < pool.Count; ++i)
        {
            PooledBuffer entry = pool[i];
            if (entry.Context != null && !entry.Context.IsDestroyed && entry.Context.IsFramePending(entry.FrameId))
                entry.Context.Wait();
            if (!entry.Buffer.IsDestroyed)
                entry.Buffer.Destroy();
        }
        pool.Clear();
    }

    private static ulong NextCapacity(ulong bytes) => (bytes + 4095ul) & ~4095ul;

    private static void ExtractQuadAxes(in Matrix4x4 matrix, out Vector2 origin, out Vector2 right, out Vector2 down)
    {
        Vector2 axisX = new(matrix.M11, matrix.M12);
        Vector2 axisY = new(matrix.M21, matrix.M22);
        Vector2 center = new(matrix.M41, matrix.M42);
        origin = center - axisX * 0.5f + axisY * 0.5f;
        right = axisX;
        down = -axisY;
    }

    private static Vector4 SliceCuts(float length, float first, float last)
    {
        float total = first + last;
        if (total >= length && total > 0f)
        {
            float middle = length * (first / total);
            return new Vector4(0f, middle, middle, length);
        }
        return new Vector4(0f, first, MathF.Max(first, length - last), length);
    }

    private static float MeasureMaxLineWidth(Font font, ReadOnlySpan<char> chars)
    {
        float maxWidth = 0f;
        int lineStart = 0;
        for (int i = 0; i <= chars.Length; ++i)
        {
            bool atEnd = i == chars.Length;
            if (!atEnd && chars[i] != '\n')
                continue;
            int lineEnd = i;
            if (lineEnd > lineStart && chars[lineEnd - 1] == '\r')
                --lineEnd;
            maxWidth = MathF.Max(maxWidth, font.GetNormalizedTextWidth(chars[lineStart..lineEnd]));
            lineStart = i + 1;
        }
        return maxWidth;
    }

    private static ColorFloat ResolveSliceColor(
        int charIndex,
        ColorFloat defaultColor,
        ReadOnlySpan<TextSlice> slices,
        ref int sliceCursor)
    {
        if (slices.IsEmpty)
            return defaultColor;

        while (sliceCursor < slices.Length &&
               charIndex >= slices[sliceCursor].Start + slices[sliceCursor].Length)
        {
            ++sliceCursor;
        }

        ColorFloat resolved = defaultColor;
        for (int i = sliceCursor; i < slices.Length; ++i)
        {
            TextSlice slice = slices[i];
            if (charIndex < slice.Start)
                break;
            if (charIndex < slice.Start + slice.Length)
                resolved = slice.Color;
        }
        return resolved;
    }
}
