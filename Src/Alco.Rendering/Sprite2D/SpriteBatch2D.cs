using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// Immediate-mode bindless 2D batcher that coalesces mixed sprites, rectangles, lines, 9-slices,
/// and SDF text glyphs into minimal vertex-pulled instanced draws while preserving submission order.
/// </summary>
public sealed unsafe class SpriteBatch2D : AutoDisposable
{
    private struct DrawRun
    {
        public int First;
        public int Count;
        public Material Material;
        public DepthStencilState DepthStencil;
        public int RootSize;
        public int SnapshotOffset;
        public bool IsCustomMaterial;
    }

    private struct RetainedFrameTextures
    {
        public RenderContext Context;
        public ulong FrameId;
        public List<Texture> Textures;
    }

    private readonly RenderingSystem _system;
    private readonly GlyphCache? _glyphCache;
    private readonly TransientBufferPool _quadBufferPool;
    private readonly Shader _defaultShader;
    private readonly Material _defaultMaterial;
    private readonly Dictionary<BlendState, Material> _blendMaterials = new();

    private readonly List<Sprite2DQuad> _quads = new(512);
    private readonly List<DrawRun> _runs = new(32);
    private readonly HashSet<Texture> _usedTextures = new(32);
    private readonly List<RetainedFrameTextures> _retainedFrames = new(4);
    private readonly List<List<Texture>> _textureListPool = new(4);

    private byte[] _materialSnapshotArena = new byte[1024];
    private int _materialSnapshotCount;
    private byte[] _tempRootScratch = new byte[256];

    private Alco.Graphics.Buffer? _activeQuadBuffer;
    private int _textGlyphCount;
    private SpriteBatch2DStats _lastStats;

    /// <summary>Creates a 2D immediate sprite batcher backed by <paramref name="system"/>.</summary>
    /// <param name="system">The rendering system owning descriptor heaps and device resources.</param>
    /// <param name="glyphCache">Optional SDF glyph cache enabling <see cref="DrawText"/> calls.</param>
    /// <param name="shaders">Optional shader system override; defaults to <see cref="RenderingSystem.Shaders"/>.</param>
    public SpriteBatch2D(RenderingSystem system, GlyphCache? glyphCache = null, ShaderSystem? shaders = null)
    {
        _system = system ?? throw new ArgumentNullException(nameof(system));
        _glyphCache = glyphCache;
        _quadBufferPool = new TransientBufferPool(system);

        ShaderSystem resolvedShaders = shaders ?? system.Shaders;
        AlcoSprite2DShaders.EnsureBuiltInModulesLoaded(resolvedShaders);
        _defaultShader = resolvedShaders.GetShader(AlcoSprite2DShaders.Sprite2DDefaultModule, ShaderKind.Graphics);

        _defaultMaterial = CreateDefaultBlendMaterial(BlendState.PremultipliedAlpha, "Sprite2D_Default_Premultiplied");
        _blendMaterials[BlendState.PremultipliedAlpha] = _defaultMaterial;

        if ((uint)sizeof(Sprite2DPassHeader) != _defaultMaterial.Root.SizeBytes ||
            !_defaultMaterial.Reflection.MatchesStructLayout<Sprite2DPassHeader>("Sprite2DDefaultRoot") ||
            _defaultMaterial.Reflection.Root?.Find("Pass.Quads")?.OffsetBytes != 64u)
        {
            throw new InvalidOperationException(
                "Sprite2DDefault shader layout does not match C# Sprite2DPassHeader.");
        }
    }

    /// <summary>Gets the telemetry recorded by the most recent <see cref="Record"/> call.</summary>
    public SpriteBatch2DStats LastStats => _lastStats;

    /// <summary>Gets the number of quads queued since the last <see cref="Begin"/> call.</summary>
    public int QueuedQuadCount => _quads.Count;

    /// <summary>Clears queued draws for a new frame and retires texture references from completed frames.</summary>
    public void Begin()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        ReclaimCompletedTextures();
        _quads.Clear();
        _runs.Clear();
        _usedTextures.Clear();
        _materialSnapshotCount = 0;
        _textGlyphCount = 0;
        _activeQuadBuffer = null;
    }

    /// <summary>Draws a texture quad positioned and oriented by <paramref name="transform"/>.</summary>
    /// <param name="texture">The texture to sample, or null for the system white texture.</param>
    /// <param name="transform">The world-space center, rotation, and scale.</param>
    /// <param name="uvRect">Normalized UV region inside <paramref name="texture"/>.</param>
    /// <param name="color">Linear RGBA tint color.</param>
    /// <param name="depth">World Z depth coordinate.</param>
    /// <param name="sampler">Sampler mode for this quad.</param>
    /// <param name="shading">Shading interpretation for this quad.</param>
    /// <param name="material">Optional custom <c>Sprite2DPass</c> surface material overriding the default shader.</param>
    /// <param name="blend">Optional blend state override when <paramref name="material"/> is null.</param>
    /// <param name="depthStencil">Optional depth-stencil state override for this draw run.</param>
    public void DrawSprite(
        Texture? texture,
        Transform2D transform,
        Rect uvRect,
        ColorFloat color,
        float depth = 0f,
        Sprite2DSamplerMode sampler = Sprite2DSamplerMode.LinearClamp,
        Sprite2DShadingKind shading = Sprite2DShadingKind.Straight,
        Material? material = null,
        BlendState? blend = null,
        DepthStencilState? depthStencil = null)
    {
        Vector2 axisX = math.rotate(new Vector2(transform.Scale.X, 0f), transform.Rotation);
        Vector2 axisY = math.rotate(new Vector2(0f, transform.Scale.Y), transform.Rotation);
        Vector2 origin = transform.Position - axisX * 0.5f + axisY * 0.5f;
        EmitQuad(origin, axisX, -axisY, depth, texture, uvRect, color, sampler, shading, material, blend, depthStencil);
    }

    /// <summary>Draws a texture quad with explicit position, rotation, and size.</summary>
    /// <param name="texture">The texture to sample, or null for the system white texture.</param>
    /// <param name="position">World-space center position.</param>
    /// <param name="rotation">World-space 2D rotation.</param>
    /// <param name="size">World-space width and height.</param>
    /// <param name="uvRect">Normalized UV region inside <paramref name="texture"/>.</param>
    /// <param name="color">Linear RGBA tint color.</param>
    /// <param name="depth">World Z depth coordinate.</param>
    /// <param name="sampler">Sampler mode for this quad.</param>
    /// <param name="shading">Shading interpretation for this quad.</param>
    /// <param name="material">Optional custom <c>Sprite2DPass</c> surface material overriding the default shader.</param>
    /// <param name="blend">Optional blend state override when <paramref name="material"/> is null.</param>
    /// <param name="depthStencil">Optional depth-stencil state override for this draw run.</param>
    public void DrawSprite(
        Texture? texture,
        Vector2 position,
        Rotation2D rotation,
        Vector2 size,
        Rect uvRect,
        ColorFloat color,
        float depth = 0f,
        Sprite2DSamplerMode sampler = Sprite2DSamplerMode.LinearClamp,
        Sprite2DShadingKind shading = Sprite2DShadingKind.Straight,
        Material? material = null,
        BlendState? blend = null,
        DepthStencilState? depthStencil = null)
    {
        DrawSprite(texture, new Transform2D(position, rotation, size), uvRect, color, depth, sampler, shading, material, blend, depthStencil);
    }

    /// <summary>Draws a <see cref="Sprite"/> using its texture, UV rectangle, filter hint, and alpha mode.</summary>
    /// <param name="sprite">The sprite region to draw.</param>
    /// <param name="transform">The world-space center, rotation, and scale.</param>
    /// <param name="color">Linear RGBA tint color.</param>
    /// <param name="depth">World Z depth coordinate.</param>
    /// <param name="addressMode">Texture addressing mode.</param>
    /// <param name="shadingOverride">Optional shading mode override (for example <see cref="Sprite2DShadingKind.Premultiplied"/>).</param>
    /// <param name="material">Optional custom <c>Sprite2DPass</c> surface material overriding the default shader.</param>
    /// <param name="blend">Optional blend state override when <paramref name="material"/> is null.</param>
    /// <param name="depthStencil">Optional depth-stencil state override for this draw run.</param>
    public void DrawSprite(
        Sprite sprite,
        Transform2D transform,
        ColorFloat color,
        float depth = 0f,
        AddressMode addressMode = AddressMode.ClampToEdge,
        Sprite2DShadingKind? shadingOverride = null,
        Material? material = null,
        BlendState? blend = null,
        DepthStencilState? depthStencil = null)
    {
        ArgumentNullException.ThrowIfNull(sprite);

        Sprite2DSamplerMode sampler = Sprite2DFlagsUtility.ResolveSamplerMode(sprite.FilterMode, addressMode);
        Sprite2DShadingKind shading = shadingOverride ?? (sprite.IsPremultiplied ? Sprite2DShadingKind.Premultiplied : Sprite2DShadingKind.Straight);
        DrawSprite(sprite.Texture, transform, sprite.UvRect, color, depth, sampler, shading, material, blend, depthStencil);
    }

    /// <summary>Draws an <see cref="Image"/> using its default full-texture region and import hints.</summary>
    /// <param name="image">The image asset to draw.</param>
    /// <param name="transform">The world-space center, rotation, and scale.</param>
    /// <param name="color">Linear RGBA tint color.</param>
    /// <param name="depth">World Z depth coordinate.</param>
    /// <param name="shadingOverride">Optional shading mode override.</param>
    /// <param name="material">Optional custom <c>Sprite2DPass</c> surface material overriding the default shader.</param>
    /// <param name="blend">Optional blend state override when <paramref name="material"/> is null.</param>
    /// <param name="depthStencil">Optional depth-stencil state override for this draw run.</param>
    public void DrawSprite(
        Image image,
        Transform2D transform,
        ColorFloat color,
        float depth = 0f,
        Sprite2DShadingKind? shadingOverride = null,
        Material? material = null,
        BlendState? blend = null,
        DepthStencilState? depthStencil = null)
    {
        ArgumentNullException.ThrowIfNull(image);

        Sprite2DSamplerMode sampler = Sprite2DFlagsUtility.ResolveSamplerMode(image.FilterMode, image.AddressMode);
        Sprite2DShadingKind shading = shadingOverride ?? (image.IsPremultiplied ? Sprite2DShadingKind.Premultiplied : Sprite2DShadingKind.Straight);
        DrawSprite(image.Texture, transform, Rect.One, color, depth, sampler, shading, material, blend, depthStencil);
    }

    /// <summary>Draws a texture quad transformed by a 2D affine <see cref="Matrix4x4"/>.</summary>
    /// <param name="texture">The texture to sample, or null for the system white texture.</param>
    /// <param name="matrix">Row-major transform matrix whose XY basis spans a unit centered quad.</param>
    /// <param name="uvRect">Normalized UV region inside <paramref name="texture"/>.</param>
    /// <param name="color">Linear RGBA tint color.</param>
    /// <param name="depth">World Z depth coordinate.</param>
    /// <param name="sampler">Sampler mode for this quad.</param>
    /// <param name="shading">Shading interpretation for this quad.</param>
    /// <param name="material">Optional custom <c>Sprite2DPass</c> surface material overriding the default shader.</param>
    /// <param name="blend">Optional blend state override when <paramref name="material"/> is null.</param>
    /// <param name="depthStencil">Optional depth-stencil state override for this draw run.</param>
    public void DrawSprite(
        Texture? texture,
        in Matrix4x4 matrix,
        Rect uvRect,
        ColorFloat color,
        float depth = 0f,
        Sprite2DSamplerMode sampler = Sprite2DSamplerMode.LinearClamp,
        Sprite2DShadingKind shading = Sprite2DShadingKind.Straight,
        Material? material = null,
        BlendState? blend = null,
        DepthStencilState? depthStencil = null)
    {
        Vector2 axisX = new(matrix.M11, matrix.M12);
        Vector2 axisY = new(matrix.M21, matrix.M22);
        Vector2 center = new(matrix.M41, matrix.M42);
        Vector2 origin = center - axisX * 0.5f + axisY * 0.5f;
        EmitQuad(origin, axisX, -axisY, depth, texture, uvRect, color, sampler, shading, material, blend, depthStencil);
    }

    /// <summary>Draws a solid colored rectangle.</summary>
    /// <param name="center">World-space center position.</param>
    /// <param name="size">World-space width and height.</param>
    /// <param name="color">Linear RGBA fill color.</param>
    /// <param name="depth">World Z depth coordinate.</param>
    /// <param name="rotation">Optional 2D rotation.</param>
    /// <param name="blend">Optional blend state override.</param>
    /// <param name="depthStencil">Optional depth-stencil state override.</param>
    public void DrawRect(
        Vector2 center,
        Vector2 size,
        ColorFloat color,
        float depth = 0f,
        Rotation2D? rotation = null,
        BlendState? blend = null,
        DepthStencilState? depthStencil = null)
    {
        DrawSprite(
            _system.TextureWhite,
            new Transform2D(center, rotation ?? Rotation2D.Identity, size),
            Rect.One,
            color,
            depth,
            Sprite2DSamplerMode.NearestClamp,
            Sprite2DShadingKind.Straight,
            material: null,
            blend,
            depthStencil);
    }

    /// <summary>Draws a solid colored line segment between <paramref name="start"/> and <paramref name="end"/>.</summary>
    /// <param name="start">World-space start endpoint.</param>
    /// <param name="end">World-space end endpoint.</param>
    /// <param name="width">Line thickness in world units.</param>
    /// <param name="color">Linear RGBA line color.</param>
    /// <param name="depth">World Z depth coordinate.</param>
    /// <param name="blend">Optional blend state override.</param>
    /// <param name="depthStencil">Optional depth-stencil state override.</param>
    public void DrawLine(
        Vector2 start,
        Vector2 end,
        float width,
        ColorFloat color,
        float depth = 0f,
        BlendState? blend = null,
        DepthStencilState? depthStencil = null)
    {
        if (width <= 0f)
        {
            return;
        }

        Vector2 direction = end - start;
        float lengthSquared = direction.LengthSquared();
        if (lengthSquared <= 1e-12f)
        {
            return;
        }

        float invLen = 1f / MathF.Sqrt(lengthSquared);
        Vector2 perp = new Vector2(-direction.Y * invLen, direction.X * invLen) * width;
        Vector2 origin = start + perp * 0.5f;
        EmitQuad(
            origin,
            direction,
            -perp,
            depth,
            _system.TextureWhite,
            Rect.One,
            color,
            Sprite2DSamplerMode.NearestClamp,
            Sprite2DShadingKind.Straight,
            customMaterial: null,
            blend,
            depthStencil);
    }

    /// <summary>Draws a 9-slice stretched texture quad keeping corner borders unstretched.</summary>
    /// <param name="texture">Source texture containing the 9-slice sprite.</param>
    /// <param name="uvRect">Normalized UV region inside <paramref name="texture"/>.</param>
    /// <param name="sliceBorders">Slice borders (Left, Top, Right, Bottom) in source texels.</param>
    /// <param name="center">World-space center position.</param>
    /// <param name="size">Target width and height in world units.</param>
    /// <param name="color">Linear RGBA tint color.</param>
    /// <param name="borderScale">World units per source texel applied to slice border thickness (1 for 1:1 pixel space).</param>
    /// <param name="depth">World Z depth coordinate.</param>
    /// <param name="rotation">Optional 2D rotation.</param>
    /// <param name="sampler">Sampler mode for the patches.</param>
    /// <param name="shading">Shading interpretation for the patches.</param>
    /// <param name="material">Optional custom <c>Sprite2DPass</c> surface material.</param>
    /// <param name="blend">Optional blend state override when <paramref name="material"/> is null.</param>
    /// <param name="depthStencil">Optional depth-stencil state override.</param>
    public void DrawNineSlice(
        Texture texture,
        Rect uvRect,
        Padding sliceBorders,
        Vector2 center,
        Vector2 size,
        ColorFloat color,
        float borderScale = 1f,
        float depth = 0f,
        Rotation2D? rotation = null,
        Sprite2DSamplerMode sampler = Sprite2DSamplerMode.LinearClamp,
        Sprite2DShadingKind shading = Sprite2DShadingKind.Straight,
        Material? material = null,
        BlendState? blend = null,
        DepthStencilState? depthStencil = null)
    {
        ArgumentNullException.ThrowIfNull(texture);

        float targetWidth = MathF.Abs(size.X);
        float targetHeight = MathF.Abs(size.Y);
        if (targetWidth <= 1e-5f || targetHeight <= 1e-5f)
        {
            return;
        }

        float sourceWidth = MathF.Abs(uvRect.Size.X) * texture.Extent.X;
        float sourceHeight = MathF.Abs(uvRect.Size.Y) * texture.Extent.Y;
        Rotation2D rot = rotation ?? Rotation2D.Identity;
        if (sourceWidth <= 0f || sourceHeight <= 0f || borderScale <= 0f)
        {
            DrawSprite(texture, new Transform2D(center, rot, size), uvRect, color, depth, sampler, shading, material, blend, depthStencil);
            return;
        }

        Vector4 srcX = SliceCuts(sourceWidth, Math.Clamp(sliceBorders.Left, 0f, sourceWidth), Math.Clamp(sliceBorders.Right, 0f, sourceWidth));
        Vector4 srcY = SliceCuts(sourceHeight, Math.Clamp(sliceBorders.Top, 0f, sourceHeight), Math.Clamp(sliceBorders.Bottom, 0f, sourceHeight));
        Vector4 dstX = SliceCuts(targetWidth, srcX.Y * borderScale, (srcX.W - srcX.Z) * borderScale);
        Vector4 dstY = SliceCuts(targetHeight, srcY.Y * borderScale, (srcY.W - srcY.Z) * borderScale);

        Vector2 axisX = math.rotate(new Vector2(size.X, 0f), rot);
        Vector2 axisY = math.rotate(new Vector2(0f, size.Y), rot);
        Vector2 unitRight = axisX / targetWidth;
        Vector2 unitDown = -axisY / targetHeight;
        Vector2 topLeft = center - axisX * 0.5f + axisY * 0.5f;

        float uScale = uvRect.Size.X / sourceWidth;
        float vScale = uvRect.Size.Y / sourceHeight;

        for (int y = 0; y < 3; ++y)
        {
            float patchHeight = dstY[y + 1] - dstY[y];
            if (patchHeight <= 1e-5f)
            {
                continue;
            }

            float v0 = uvRect.Origin.Y + srcY[y] * vScale;
            float dv = (srcY[y + 1] - srcY[y]) * vScale;
            for (int x = 0; x < 3; ++x)
            {
                float patchWidth = dstX[x + 1] - dstX[x];
                if (patchWidth <= 1e-5f)
                {
                    continue;
                }

                Rect patchUv = new(uvRect.Origin.X + srcX[x] * uScale, v0, (srcX[x + 1] - srcX[x]) * uScale, dv);
                Vector2 patchOrigin = topLeft + unitRight * dstX[x] + unitDown * dstY[y];
                EmitQuad(
                    patchOrigin,
                    unitRight * patchWidth,
                    unitDown * patchHeight,
                    depth,
                    texture,
                    patchUv,
                    color,
                    sampler,
                    shading,
                    material,
                    blend,
                    depthStencil);
            }
        }
    }

    /// <summary>Draws a 9-slice stretched <see cref="Sprite"/> keeping corner borders unstretched.</summary>
    /// <param name="sprite">The sprite with slice padding and filter settings.</param>
    /// <param name="center">World-space center position.</param>
    /// <param name="size">Target width and height in world units.</param>
    /// <param name="color">Linear RGBA tint color.</param>
    /// <param name="borderScale">World units per source texel applied to slice border thickness.</param>
    /// <param name="depth">World Z depth coordinate.</param>
    /// <param name="rotation">Optional 2D rotation.</param>
    /// <param name="material">Optional custom <c>Sprite2DPass</c> surface material.</param>
    /// <param name="blend">Optional blend state override when <paramref name="material"/> is null.</param>
    /// <param name="depthStencil">Optional depth-stencil state override.</param>
    public void DrawNineSlice(
        Sprite sprite,
        Vector2 center,
        Vector2 size,
        ColorFloat color,
        float borderScale = 1f,
        float depth = 0f,
        Rotation2D? rotation = null,
        Material? material = null,
        BlendState? blend = null,
        DepthStencilState? depthStencil = null)
    {
        ArgumentNullException.ThrowIfNull(sprite);

        Sprite2DSamplerMode sampler = Sprite2DFlagsUtility.ResolveSamplerMode(sprite.FilterMode);
        Sprite2DShadingKind shading = sprite.IsPremultiplied ? Sprite2DShadingKind.Premultiplied : Sprite2DShadingKind.Straight;
        DrawNineSlice(
            sprite.Texture,
            sprite.UvRect,
            sprite.SlicePadding,
            center,
            size,
            color,
            borderScale,
            depth,
            rotation,
            sampler,
            shading,
            material,
            blend,
            depthStencil);
    }

    /// <summary>Draws SDF or bitmap text into the same quad batch using the configured <see cref="GlyphCache"/>.</summary>
    /// <param name="font">The font asset.</param>
    /// <param name="text">The UTF-16 character span to draw.</param>
    /// <param name="position">World-space anchor position.</param>
    /// <param name="fontSize">Em height in world units.</param>
    /// <param name="color">Default linear RGBA text color.</param>
    /// <param name="pivot">Anchor pivot relative to the laid-out text bounds; defaults to <see cref="Pivot.Center"/>.</param>
    /// <param name="rotation">Optional 2D rotation around <paramref name="position"/>.</param>
    /// <param name="depth">World Z depth coordinate.</param>
    /// <param name="lineSpacing">Line-to-line spacing in em units.</param>
    /// <param name="slices">Optional per-character color override slices.</param>
    /// <param name="material">Optional custom <c>Sprite2DPass</c> surface material.</param>
    /// <param name="blend">Optional blend state override when <paramref name="material"/> is null.</param>
    /// <param name="depthStencil">Optional depth-stencil state override.</param>
    /// <returns>The maximum line width in world units.</returns>
    public float DrawText(
        Font font,
        ReadOnlySpan<char> text,
        Vector2 position,
        float fontSize,
        ColorFloat color,
        Pivot? pivot = null,
        Rotation2D? rotation = null,
        float depth = 0f,
        float lineSpacing = 1f,
        ReadOnlySpan<TextSlice> slices = default,
        Material? material = null,
        BlendState? blend = null,
        DepthStencilState? depthStencil = null)
    {
        ArgumentNullException.ThrowIfNull(font);
        if (text.IsEmpty || fontSize <= 0f)
        {
            return 0f;
        }

        if (_glyphCache == null)
        {
            throw new InvalidOperationException("DrawText requires a GlyphCache passed to the SpriteBatch2D constructor.");
        }

        Rotation2D rot = rotation ?? Rotation2D.Identity;
        Pivot resolvedPivot = pivot ?? Pivot.Center;
        Vector2 axisX = math.rotate(new Vector2(fontSize, 0f), rot);
        Vector2 axisY = math.rotate(new Vector2(0f, fontSize), rot);

        int lineCount = 1;
        for (int i = 0; i < text.Length; ++i)
        {
            if (text[i] == '\n')
            {
                ++lineCount;
            }
        }

        float totalHeight = font.Ascent + font.Descent;
        float baselineY = (0.5f - resolvedPivot.value.Y) * totalHeight - font.Ascent +
            (lineCount - 1) * lineSpacing * (0.5f - resolvedPivot.value.Y);

        float maxAdvance = 0f;
        int lineStart = 0;
        int sliceCursor = 0;

        for (int i = 0; i <= text.Length; ++i)
        {
            bool atEnd = i == text.Length;
            if (!atEnd && text[i] != '\n')
            {
                continue;
            }

            int lineEnd = i;
            if (lineEnd > lineStart && text[lineEnd - 1] == '\r')
            {
                --lineEnd;
            }

            ReadOnlySpan<char> lineSpan = text[lineStart..lineEnd];
            float lineWidth = font.GetNormalizedTextWidth(lineSpan);
            maxAdvance = MathF.Max(maxAdvance, lineWidth);

            float penX = -(0.5f + resolvedPivot.value.X) * lineWidth;
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
                    Vector2 worldOrigin = position + axisX * emTopLeft.X + axisY * emTopLeft.Y;
                    Vector2 right = axisX * glyph.Size.X;
                    Vector2 down = -axisY * glyph.Size.Y;
                    Rect uv = new(glyph.UvRect.X, glyph.UvRect.Y, glyph.UvRect.Z, glyph.UvRect.W);
                    EmitQuad(
                        worldOrigin,
                        right,
                        down,
                        depth,
                        glyph.Page,
                        uv,
                        charColor,
                        Sprite2DSamplerMode.LinearClamp,
                        glyph.IsDistanceField ? Sprite2DShadingKind.DistanceGlyph : Sprite2DShadingKind.BitmapGlyph,
                        material,
                        blend,
                        depthStencil);
                    ++_textGlyphCount;
                }

                penX += glyph.Advance;
                offsetInChars += consumed;
            }

            baselineY -= lineSpacing;
            lineStart = i + 1;
        }

        return maxAdvance * fontSize;
    }

    /// <summary>Uploads accumulated quads into a frame-safe transient GPU buffer.</summary>
    /// <param name="context">The active recording context.</param>
    public void Upload(RenderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        if (_quads.Count == 0)
        {
            _activeQuadBuffer = null;
            return;
        }

        ulong requiredBytes = checked((ulong)_quads.Count * (ulong)sizeof(Sprite2DQuad));
        _activeQuadBuffer = _quadBufferPool.Acquire(context, requiredBytes);
        _activeQuadBuffer.SetData<Sprite2DQuad>(CollectionsMarshal.AsSpan(_quads));

        if (_usedTextures.Count > 0)
        {
            List<Texture> retained = RentTextureList(_usedTextures.Count);
            foreach (Texture tex in _usedTextures)
            {
                retained.Add(tex);
            }

            _retainedFrames.Add(new RetainedFrameTextures
            {
                Context = context,
                FrameId = context.CurrentFrameId,
                Textures = retained,
            });
        }
    }

    /// <summary>Records coalesced instanced draw calls for all queued quads into <paramref name="render"/>.</summary>
    /// <param name="context">The active recording context.</param>
    /// <param name="render">The open render scope.</param>
    /// <param name="camera">Camera providing the view-projection matrix.</param>
    /// <param name="time">Elapsed time passed to custom surface shaders.</param>
    /// <returns>The telemetry stats for this recording.</returns>
    public SpriteBatch2DStats Record(
        RenderContext context,
        in RenderContext.RenderScope render,
        ICamera camera,
        float time = 0f)
    {
        ArgumentNullException.ThrowIfNull(camera);
        return Record(context, in render, camera.ViewProjectionMatrix, time);
    }

    /// <summary>Records coalesced instanced draw calls for all queued quads into <paramref name="render"/>.</summary>
    /// <param name="context">The active recording context.</param>
    /// <param name="render">The open render scope.</param>
    /// <param name="viewProjection">Row-major camera view-projection matrix.</param>
    /// <param name="time">Elapsed time passed to custom surface shaders.</param>
    /// <returns>The telemetry stats for this recording.</returns>
    public SpriteBatch2DStats Record(
        RenderContext context,
        in RenderContext.RenderScope render,
        in Matrix4x4 viewProjection,
        float time = 0f)
    {
        ArgumentNullException.ThrowIfNull(context);
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        if (_runs.Count == 0 || _activeQuadBuffer == null)
        {
            _lastStats = new SpriteBatch2DStats(_quads.Count, 0, 0, _textGlyphCount);
            return _lastStats;
        }

        int drawCalls = 0;
        int customMaterialDraws = 0;
        float sdfUnitRange = _glyphCache != null
            ? _glyphCache.SdfUnitRange
            : (2.0f * GlyphCacheOptions.Default.SdfSpread) / GlyphCacheOptions.Default.PageSize;
        ReadOnlySpan<DrawRun> runs = CollectionsMarshal.AsSpan(_runs);
        GPUBufferView baseView = _activeQuadBuffer.View;

        for (int i = 0; i < runs.Length; ++i)
        {
            ref readonly DrawRun run = ref runs[i];
            GPUBufferView sliceView = baseView.OffsetBy(checked((uint)(run.First * sizeof(Sprite2DQuad))));
            Sprite2DPassHeader header = new()
            {
                ViewProjection = viewProjection,
                Quads = sliceView,
                Time = time,
                SdfUnitRange = sdfUnitRange,
            };

            if (!run.IsCustomMaterial)
            {
                render.Draw(run.Material, in header, run.DepthStencil, vertexCount: 6u, instanceCount: (uint)run.Count);
            }
            else
            {
                Span<byte> dest = context.AllocateTransient<byte>(run.RootSize, out BufferSlice rootSlice);
                MemoryMarshal.Write(dest, in header);
                int tailLen = run.RootSize - sizeof(Sprite2DPassHeader);
                if (tailLen > 0)
                {
                    _materialSnapshotArena.AsSpan(run.SnapshotOffset, tailLen)
                        .CopyTo(dest.Slice(sizeof(Sprite2DPassHeader), tailLen));
                }

                render.Draw(run.Material, rootSlice, run.DepthStencil, vertexCount: 6u, instanceCount: (uint)run.Count);
                ++customMaterialDraws;
            }
            ++drawCalls;
        }

        _lastStats = new SpriteBatch2DStats(_quads.Count, drawCalls, customMaterialDraws, _textGlyphCount);
        return _lastStats;
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (!disposing)
        {
            return;
        }

        _quadBufferPool.Dispose();
        _usedTextures.Clear();
        _retainedFrames.Clear();
        _textureListPool.Clear();
        _blendMaterials.Clear();
        _quads.Clear();
        _runs.Clear();
    }

    private void EmitQuad(
        Vector2 origin,
        Vector2 right,
        Vector2 down,
        float depth,
        Texture? texture,
        Rect uvRect,
        ColorFloat color,
        Sprite2DSamplerMode sampler,
        Sprite2DShadingKind shading,
        Material? customMaterial,
        BlendState? blend,
        DepthStencilState? depthStencil)
    {
        if (right.LengthSquared() <= 1e-12f && down.LengthSquared() <= 1e-12f)
        {
            return;
        }

        bool isCustom = customMaterial != null;
        Material drawMaterial;
        int rootSize = sizeof(Sprite2DPassHeader);
        int tailLen = 0;

        if (isCustom)
        {
            drawMaterial = customMaterial!;
            if (drawMaterial.Shader.Kind != ShaderKind.Graphics || drawMaterial.Root.SizeBytes < (uint)sizeof(Sprite2DPassHeader))
            {
                throw new ArgumentException(
                    $"Sprite2D custom material '{drawMaterial.Name}' must be graphics with root size >= {sizeof(Sprite2DPassHeader)} bytes (starting with Sprite2DPassHeader).",
                    nameof(customMaterial));
            }

            rootSize = (int)drawMaterial.Root.SizeBytes;
            tailLen = rootSize - sizeof(Sprite2DPassHeader);
            if (_tempRootScratch.Length < rootSize)
            {
                _tempRootScratch = new byte[Math.Max(rootSize, _tempRootScratch.Length * 2)];
            }

            drawMaterial.CopyRoot(_tempRootScratch.AsSpan(0, rootSize));
        }
        else
        {
            drawMaterial = ResolveDefaultMaterial(blend);
        }

        DepthStencilState resolvedDepth = depthStencil ?? drawMaterial.DepthStencilState;
        Texture resolvedTexture = texture ?? _system.TextureWhite;
        if (resolvedTexture.SampledIndex == Texture.NoIndex)
        {
            throw new InvalidOperationException("SpriteBatch2D texture has no sampled descriptor slot.");
        }

        _usedTextures.Add(resolvedTexture);

        int quadIndex = _quads.Count;
        _quads.Add(new Sprite2DQuad
        {
            Origin = origin,
            Right = right,
            Down = down,
            Depth = depth,
            TextureIndex = resolvedTexture.SampledIndex,
            UvRect = new Vector4(uvRect.Origin.X, uvRect.Origin.Y, uvRect.Size.X, uvRect.Size.Y),
            Color = new Vector4(color.R, color.G, color.B, color.A),
            Flags = Sprite2DFlagsUtility.PackFlags(sampler, shading),
        });

        ReadOnlySpan<byte> tailBytes = tailLen > 0 ? _tempRootScratch.AsSpan(sizeof(Sprite2DPassHeader), tailLen) : default;
        if (_runs.Count > 0)
        {
            ref DrawRun prev = ref CollectionsMarshal.AsSpan(_runs)[^1];
            if (ReferenceEquals(prev.Material, drawMaterial) &&
                prev.DepthStencil == resolvedDepth &&
                prev.IsCustomMaterial == isCustom &&
                prev.RootSize == rootSize &&
                (tailLen == 0 || _materialSnapshotArena.AsSpan(prev.SnapshotOffset, tailLen).SequenceEqual(tailBytes)))
            {
                ++prev.Count;
                return;
            }
        }

        int snapshotOffset = tailLen > 0 ? AppendMaterialSnapshot(tailBytes) : 0;
        _runs.Add(new DrawRun
        {
            First = quadIndex,
            Count = 1,
            Material = drawMaterial,
            DepthStencil = resolvedDepth,
            RootSize = rootSize,
            SnapshotOffset = snapshotOffset,
            IsCustomMaterial = isCustom,
        });
    }

    private Material ResolveDefaultMaterial(BlendState? blend)
    {
        if (!blend.HasValue)
        {
            return _defaultMaterial;
        }

        BlendState targetBlend = blend.Value;
        if (!_blendMaterials.TryGetValue(targetBlend, out Material? material))
        {
            material = CreateDefaultBlendMaterial(targetBlend, $"Sprite2D_Default_{_blendMaterials.Count}");
            _blendMaterials.Add(targetBlend, material);
        }

        return material;
    }

    private Material CreateDefaultBlendMaterial(BlendState blend, string name)
    {
        Material material = _defaultShader.CreateMaterial(name);
        material.Blend = blend;
        material.Rasterization = RasterizationState.CullNone;
        material.DepthStencilState = DepthStencilState.None;
        return material;
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

    private void ReclaimCompletedTextures()
    {
        for (int i = _retainedFrames.Count - 1; i >= 0; --i)
        {
            RetainedFrameTextures entry = _retainedFrames[i];
            if (!entry.Context.IsFrameCompleted(entry.FrameId))
            {
                continue;
            }

            entry.Textures.Clear();
            _textureListPool.Add(entry.Textures);
            _retainedFrames.RemoveAt(i);
        }
    }

    private List<Texture> RentTextureList(int capacity)
    {
        if (_textureListPool.Count > 0)
        {
            List<Texture> list = _textureListPool[^1];
            _textureListPool.RemoveAt(_textureListPool.Count - 1);
            list.EnsureCapacity(capacity);
            return list;
        }

        return new List<Texture>(capacity);
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

    private static ColorFloat ResolveSliceColor(
        int charIndex,
        ColorFloat defaultColor,
        ReadOnlySpan<TextSlice> slices,
        ref int sliceCursor)
    {
        if (slices.IsEmpty)
        {
            return defaultColor;
        }

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
            {
                break;
            }

            if (charIndex < slice.Start + slice.Length)
            {
                resolved = slice.Color;
            }
        }

        return resolved;
    }
}
