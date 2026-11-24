using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using Alco.Graphics;
using Alco.Rendering;
using Buffer = Alco.Graphics.Buffer;

namespace Alco.ImGUI;

/// <summary>
/// Renderer that bridges ImGui draw data to <see cref="RenderingSystem"/> and manages the ImGui font atlas.
/// </summary>
public sealed unsafe class ImGUIRenderer : AutoDisposable
{
    private const ulong MinVertexBufferBytes = 16 * 1024;
    private const ulong MinIndexBufferBytes = 8 * 1024;
    private const float FontSizePixels = 18f;
    private static readonly IntPtr ResetRenderStateCallback = (IntPtr)(-8);

    /// <summary>Gets the active renderer instance, or null when no renderer exists.</summary>
    public static ImGUIRenderer? Instance { get; private set; }

    private readonly RenderingSystem _renderingSystem;
    private readonly MaterialInstance _material;
    private readonly string _name;
    private readonly HashSet<FontLanguage> _addedLanguages = new();
    private readonly List<PooledBuffer> _vertexBufferPool = new(2);
    private readonly List<PooledBuffer> _indexBufferPool = new(2);

    private IntPtr _imGuiContext;
    private Texture _fontTexture;
    private bool _fontTextureDirty;
    private RenderContext? _lastDrawContext;
    private ulong _lastDrawFrameId;

    /// <summary>
    /// Gets whether the finalized draw data for the current frame contains commands to render.
    /// </summary>
    public bool HasDrawData
    {
        get
        {
            if (IsDisposed || _imGuiContext == IntPtr.Zero)
            {
                return false;
            }

            EnsureCurrentContext();
            ImDrawDataPtr drawData = ImGui.GetDrawData();
            return drawData.NativePtr != null && drawData.CmdListsCount > 0;
        }
    }

    /// <summary>
    /// Creates the ImGui context and initial font atlas for rendering through <see cref="RenderingSystem"/>.
    /// </summary>
    /// <param name="renderingSystem">The rendering system that owns the GPU resources and descriptor heaps.</param>
    /// <param name="material">The ImGui material whose shader and blend state are inherited.</param>
    /// <param name="name">The diagnostic name prefix used for renderer GPU resources.</param>
    public ImGUIRenderer(RenderingSystem renderingSystem, Material material, string name = "ImGUIRenderer")
    {
        ArgumentNullException.ThrowIfNull(renderingSystem);
        ArgumentNullException.ThrowIfNull(material);
        if (Instance != null)
        {
            throw new InvalidOperationException("ImGUIRenderer can only have one instance at a time.");
        }

        Debug.Assert(sizeof(ImDrawVert) == 20 && sizeof(ImGuiRootArguments) == 32);

        _renderingSystem = renderingSystem;
        _material = material.CreateInstance();
        _name = string.IsNullOrEmpty(name) ? "ImGUIRenderer" : name;

        _imGuiContext = ImGui.CreateContext();
        try
        {
            ImGui.SetCurrentContext(_imGuiContext);
            ImGuiNative.InitializeErrorHandling();

            ImGuiIOPtr io = ImGui.GetIO();
            io.ConfigFlags |= ImGuiConfigFlags.DockingEnable;
            io.BackendFlags |= ImGuiBackendFlags.RendererHasVtxOffset;

            io.ConfigErrorRecovery = true;
            io.ConfigErrorRecoveryEnableAssert = false;
            io.ConfigErrorRecoveryEnableDebugLog = true;
            io.ConfigErrorRecoveryEnableTooltip = true;

            // Embedded fallback font until the project font loads via AddFontForLanguage(Basic).
            io.Fonts.AddFontDefault();
            io.Fonts.Flags |= ImFontAtlasFlags.NoBakedLines;

            _fontTexture = BuildFontTexture(io);
            Instance = this;
        }
        catch
        {
            if (_fontTexture != null && !_fontTexture.IsDisposed)
            {
                _fontTexture.Dispose();
            }
            if (_imGuiContext != IntPtr.Zero)
            {
                ImGui.DestroyContext(_imGuiContext);
                _imGuiContext = IntPtr.Zero;
            }
            throw;
        }
    }

    /// <summary>Starts a new ImGui frame with unit framebuffer scale.</summary>
    /// <param name="width">The display width in logical pixels.</param>
    /// <param name="height">The display height in logical pixels.</param>
    /// <param name="deltaTime">Elapsed time since the previous frame, in seconds.</param>
    public void Begin(uint width, uint height, float deltaTime)
    {
        Begin(width, height, deltaTime, Vector2.One);
    }

    /// <summary>Starts a new ImGui frame with the given display dimensions and framebuffer scale.</summary>
    /// <param name="width">The display width in logical pixels.</param>
    /// <param name="height">The display height in logical pixels.</param>
    /// <param name="deltaTime">Elapsed time since the previous frame, in seconds.</param>
    /// <param name="framebufferScale">Ratio of physical framebuffer pixels to logical display coordinates.</param>
    public void Begin(uint width, uint height, float deltaTime, Vector2 framebufferScale)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        EnsureCurrentContext();

        if (_fontTextureDirty)
        {
            UpdateFontTexture();
            _fontTextureDirty = false;
        }

        ImGuiIOPtr io = ImGui.GetIO();
        io.DisplaySize = new Vector2(width, height);
        io.DisplayFramebufferScale = framebufferScale;
        io.DeltaTime = deltaTime > 0.0f ? deltaTime : 1.0f / 60.0f;

        ImGui.NewFrame();
        Gizmo.BeginFrame(width, height);
    }

    /// <summary>
    /// Finalizes the ImGui frame. Must be called every frame after <see cref="Begin(uint, uint, float)"/>.
    /// </summary>
    public void Render()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        EnsureCurrentContext();
        ImGui.Render();
    }

    /// <summary>
    /// Records an overlay render pass that loads existing contents and draws ImGui geometry on top.
    /// </summary>
    /// <param name="context">An active render context between scopes.</param>
    /// <param name="target">The color render target to draw into.</param>
    /// <param name="insertBarrier">Whether to emit a barrier ensuring prior writes are visible before blending.</param>
    public void Draw(RenderContext context, RenderTarget target, bool insertBarrier = true)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        ArgumentNullException.ThrowIfNull(context);
        if (!target.IsValid)
        {
            return;
        }

        Extent2D targetExtent = target.Extent;
        if (targetExtent.X == 0 || targetExtent.Y == 0)
        {
            return;
        }

        EnsureCurrentContext();
        ImDrawDataPtr drawData = ImGui.GetDrawData();
        if (drawData.NativePtr == null || drawData.CmdListsCount <= 0)
        {
            return;
        }

        if (insertBarrier)
        {
            context.Barrier(
                Stage.ColorOutput | Stage.Compute | Stage.Transfer,
                Access.ColorWrite | Access.ShaderWrite | Access.TransferWrite,
                Stage.Pixel | Stage.ColorOutput,
                Access.ShaderRead | Access.ColorRead | Access.ColorWrite);
        }

        RenderingDesc pass = new()
        {
            Colors =
            [
                new ColorAttachment
                {
                    Target = target,
                    Load = LoadOp.Load,
                    Store = StoreOp.Store,
                },
            ],
        };

        using RenderContext.RenderScope render = context.BeginRenderPass(pass);
        Draw(in render, context, targetExtent, drawData);
    }

    /// <summary>
    /// Records ImGui draw commands for the current frame into an already-open render pass.
    /// </summary>
    /// <param name="render">The active render scope.</param>
    /// <param name="context">The recording context owning <paramref name="render"/>.</param>
    /// <param name="targetExtent">The render target extent in physical pixels.</param>
    public void Draw(in RenderContext.RenderScope render, RenderContext context, Extent2D targetExtent)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        EnsureCurrentContext();
        Draw(in render, context, targetExtent, ImGui.GetDrawData());
    }

    /// <summary>
    /// Records explicit ImGui draw data into an already-open render pass.
    /// </summary>
    /// <param name="render">The active render scope.</param>
    /// <param name="context">The recording context owning <paramref name="render"/>.</param>
    /// <param name="targetExtent">The render target extent in physical pixels.</param>
    /// <param name="drawData">The finalized ImGui draw data.</param>
    public void Draw(in RenderContext.RenderScope render, RenderContext context, Extent2D targetExtent, ImDrawDataPtr drawData)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        ArgumentNullException.ThrowIfNull(context);

        if (drawData.NativePtr == null || drawData.CmdListsCount <= 0 ||
            targetExtent.X == 0 || targetExtent.Y == 0 ||
            drawData.DisplaySize.X <= 0.0f || drawData.DisplaySize.Y <= 0.0f)
        {
            return;
        }

        Buffer? vertexBuffer = drawData.TotalVtxCount > 0
            ? AcquireTransientBuffer(
                _vertexBufferPool,
                context,
                checked((ulong)drawData.TotalVtxCount * (ulong)sizeof(ImDrawVert)),
                MinVertexBufferBytes)
            : null;
        Buffer? indexBuffer = drawData.TotalIdxCount > 0
            ? AcquireTransientBuffer(
                _indexBufferPool,
                context,
                checked((ulong)drawData.TotalIdxCount * sizeof(ushort)),
                MinIndexBufferBytes)
            : null;

        ulong vertexWriteBytes = 0;
        ulong indexWriteBytes = 0;
        for (int i = 0; i < drawData.CmdListsCount; i++)
        {
            ImDrawListPtr cmdList = drawData.CmdLists[i];
            if (vertexBuffer != null && cmdList.VtxBuffer.Size > 0)
            {
                ReadOnlySpan<ImDrawVert> vertices = new((void*)cmdList.VtxBuffer.Data, cmdList.VtxBuffer.Size);
                vertexBuffer.SetData(vertices, vertexWriteBytes);
                vertexWriteBytes += (ulong)cmdList.VtxBuffer.Size * (ulong)sizeof(ImDrawVert);
            }

            if (indexBuffer != null && cmdList.IdxBuffer.Size > 0)
            {
                ReadOnlySpan<ushort> indices = new((void*)cmdList.IdxBuffer.Data, cmdList.IdxBuffer.Size);
                indexBuffer.SetData(indices, indexWriteBytes);
                indexWriteBytes += (ulong)cmdList.IdxBuffer.Size * sizeof(ushort);
            }
        }

        _lastDrawContext = context;
        _lastDrawFrameId = context.CurrentFrameId;

        render.SetViewport(new Viewport { Width = targetExtent.X, Height = targetExtent.Y });
        render.SetScissor(new Scissor { Width = targetExtent.X, Height = targetExtent.Y });

        Vector2 scale = new(2.0f / drawData.DisplaySize.X, -2.0f / drawData.DisplaySize.Y);
        Vector2 translate = new Vector2(-1.0f, 1.0f) - drawData.DisplayPos * scale;
        uint descriptorCapacity = _renderingSystem.TextureDescriptorCapacity;
        uint whiteTextureIndex = _renderingSystem.TextureWhite.SampledIndex;

        uint vertexOffset = 0;
        uint indexOffset = 0;
        for (int i = 0; i < drawData.CmdListsCount; i++)
        {
            ImDrawListPtr cmdList = drawData.CmdLists[i];
            for (int j = 0; j < cmdList.CmdBuffer.Size; j++)
            {
                ImDrawCmdPtr cmd = cmdList.CmdBuffer[j];
                if (cmd.UserCallback != IntPtr.Zero)
                {
                    if (cmd.UserCallback == ResetRenderStateCallback)
                    {
                        render.SetViewport(new Viewport { Width = targetExtent.X, Height = targetExtent.Y });
                        render.SetScissor(new Scissor { Width = targetExtent.X, Height = targetExtent.Y });
                    }
                    else
                    {
                        ((delegate* unmanaged[Cdecl]<ImDrawList*, ImDrawCmd*, void>)cmd.UserCallback)(cmdList.NativePtr, cmd.NativePtr);
                    }
                    continue;
                }

                int left = (int)Math.Clamp((cmd.ClipRect.X - drawData.DisplayPos.X) * drawData.FramebufferScale.X, 0.0f, targetExtent.X);
                int top = (int)Math.Clamp((cmd.ClipRect.Y - drawData.DisplayPos.Y) * drawData.FramebufferScale.Y, 0.0f, targetExtent.Y);
                int right = (int)Math.Clamp((cmd.ClipRect.Z - drawData.DisplayPos.X) * drawData.FramebufferScale.X, 0.0f, targetExtent.X);
                int bottom = (int)Math.Clamp((cmd.ClipRect.W - drawData.DisplayPos.Y) * drawData.FramebufferScale.Y, 0.0f, targetExtent.Y);
                if (right <= left || bottom <= top || cmd.ElemCount == 0 || vertexBuffer == null || indexBuffer == null)
                {
                    continue;
                }

                nint rawTextureId = cmd.TextureId;
                uint textureIndex = rawTextureId >= 0 && (ulong)rawTextureId < descriptorCapacity
                    ? (uint)rawTextureId
                    : whiteTextureIndex;

                uint baseVertex = checked(vertexOffset + cmd.VtxOffset);
                ImGuiRootArguments root = new()
                {
                    Scale = scale,
                    Translate = translate,
                    Vertices = vertexBuffer.Slice(checked((ulong)baseVertex * (ulong)sizeof(ImDrawVert))).View,
                    Texture = textureIndex,
                };

                render.SetScissor(new Scissor
                {
                    X = left,
                    Y = top,
                    Width = (uint)(right - left),
                    Height = (uint)(bottom - top),
                });
                render.DrawIndexed(
                    _material,
                    in root,
                    default,
                    indexBuffer,
                    IndexType.UInt16,
                    cmd.ElemCount,
                    firstIndex: checked(indexOffset + cmd.IdxOffset));
            }

            vertexOffset += (uint)cmdList.VtxBuffer.Size;
            indexOffset += (uint)cmdList.IdxBuffer.Size;
        }
    }

    /// <summary>
    /// Adds a font with the given predefined language glyph range at the unified UI size,
    /// merging additional languages into the default font; each language is added at most once.
    /// </summary>
    /// <param name="fontData">Font bytes.</param>
    /// <param name="language">Predefined language range.</param>
    public void AddFontForLanguage(ReadOnlySpan<byte> fontData, FontLanguage language)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        EnsureCurrentContext();

        if (_addedLanguages.Contains(language))
        {
            return;
        }

        ImGuiIOPtr io = ImGui.GetIO();
        if (language == FontLanguage.Basic)
        {
            _addedLanguages.Add(language);
            ImFontConfigPtr cfg = new(ImGuiNative.ImFontConfig_ImFontConfig());
            cfg.FontDataOwnedByAtlas = false;
            IntPtr basicRanges = io.Fonts.GetGlyphRangesDefault();
            fixed (byte* fontDataPtr = fontData)
            {
                ImFontPtr font = io.Fonts.AddFontFromMemoryTTF((IntPtr)fontDataPtr, fontData.Length, FontSizePixels, cfg, basicRanges);
                io.NativePtr->FontDefault = font;
            }
            cfg.Destroy();
            _fontTextureDirty = true;
            return;
        }

        IntPtr ranges = language switch
        {
            FontLanguage.Chinese => io.Fonts.GetGlyphRangesChineseFull(),
            FontLanguage.Japanese => io.Fonts.GetGlyphRangesJapanese(),
            FontLanguage.Korean => io.Fonts.GetGlyphRangesKorean(),
            FontLanguage.Cyrillic => io.Fonts.GetGlyphRangesCyrillic(),
            FontLanguage.Greek => io.Fonts.GetGlyphRangesGreek(),
            FontLanguage.Thai => io.Fonts.GetGlyphRangesThai(),
            FontLanguage.Vietnamese => io.Fonts.GetGlyphRangesVietnamese(),
            _ => io.Fonts.GetGlyphRangesDefault(),
        };

        fixed (byte* fontDataPtr = fontData)
        {
            // Merge glyphs into the default font to keep a single active font.
            ImFontConfigPtr cfg = new(ImGuiNative.ImFontConfig_ImFontConfig());
            cfg.MergeMode = true;
            cfg.FontDataOwnedByAtlas = false;
            io.Fonts.AddFontFromMemoryTTF((IntPtr)fontDataPtr, fontData.Length, FontSizePixels, cfg, ranges);
            cfg.Destroy();
        }
        _addedLanguages.Add(language);
        _fontTextureDirty = true;
    }

    private Texture BuildFontTexture(ImGuiIOPtr io)
    {
        io.Fonts.GetTexDataAsRGBA32(out byte* pixels, out int width, out int height, out int bytesPerPixel);
        ReadOnlySpan<byte> fontTextureData = new(pixels, checked(width * height * bytesPerPixel));

        Texture texture = TextureUtility.CreateTexture(
            _renderingSystem,
            fontTextureData,
            (uint)width,
            (uint)height,
            Format.Rgba8Unorm,
            TextureUsage.Sampled | TextureUsage.TransferDestination);
        io.Fonts.SetTexID((IntPtr)texture.SampledIndex);
        io.Fonts.ClearTexData();
        io.Fonts.TexReady = true;
        return texture;
    }

    private void UpdateFontTexture()
    {
        ImGuiIOPtr io = ImGui.GetIO();
        Texture nextTexture = BuildFontTexture(io);
        Texture previousTexture = _fontTexture;
        _fontTexture = nextTexture;

        if (previousTexture != null && !previousTexture.IsDisposed)
        {
            previousTexture.Dispose();
        }
    }

    private Buffer AcquireTransientBuffer(List<PooledBuffer> pool, RenderContext context, ulong requiredBytes, ulong minBytes)
    {
        for (int i = 0; i < pool.Count; i++)
        {
            PooledBuffer entry = pool[i];
            if (entry.Context != null && !entry.Context.IsDisposed && entry.Context.IsFramePending(entry.FrameId))
            {
                continue;
            }

            if (entry.Buffer.SizeBytes < requiredBytes)
            {
                ulong grownBytes = NextCapacity(requiredBytes, Math.Max(minBytes, entry.Buffer.SizeBytes * 2));
                entry.Buffer.Dispose();
                entry.Buffer = _renderingSystem.CreateBuffer(grownBytes);
            }

            entry.Context = context;
            entry.FrameId = context.CurrentFrameId;
            pool[i] = entry;
            return entry.Buffer;
        }

        ulong initialBytes = NextCapacity(requiredBytes, minBytes);
        Buffer created = _renderingSystem.CreateBuffer(initialBytes);
        pool.Add(new PooledBuffer(context, context.CurrentFrameId, created));
        return created;
    }

    private static ulong NextCapacity(ulong requiredBytes, ulong minBytes)
    {
        ulong capacity = Math.Max(requiredBytes, minBytes);
        return (capacity + 4095ul) & ~4095ul;
    }

    private void EnsureCurrentContext()
    {
        if (ImGui.GetCurrentContext() != _imGuiContext)
        {
            ImGui.SetCurrentContext(_imGuiContext);
        }
    }

    private static void WaitIfPending(RenderContext? context, ulong frameId)
    {
        if (context != null && !context.IsDisposed && context.IsFramePending(frameId))
        {
            context.Wait();
        }
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (ReferenceEquals(Instance, this))
        {
            Instance = null;
        }

        if (disposing)
        {
            WaitIfPending(_lastDrawContext, _lastDrawFrameId);
            for (int i = 0; i < _vertexBufferPool.Count; i++)
            {
                WaitIfPending(_vertexBufferPool[i].Context, _vertexBufferPool[i].FrameId);
                if (!_vertexBufferPool[i].Buffer.IsDisposed)
                {
                    _vertexBufferPool[i].Buffer.Dispose();
                }
            }
            _vertexBufferPool.Clear();

            for (int i = 0; i < _indexBufferPool.Count; i++)
            {
                WaitIfPending(_indexBufferPool[i].Context, _indexBufferPool[i].FrameId);
                if (!_indexBufferPool[i].Buffer.IsDisposed)
                {
                    _indexBufferPool[i].Buffer.Dispose();
                }
            }
            _indexBufferPool.Clear();

            if (_fontTexture != null && !_fontTexture.IsDisposed)
            {
                _fontTexture.Dispose();
            }

            if (_imGuiContext != IntPtr.Zero)
            {
                EnsureCurrentContext();
                ImGuiIOPtr io = ImGui.GetIO();
                io.Fonts.SetTexID(IntPtr.Zero);
                io.BackendFlags &= ~ImGuiBackendFlags.RendererHasVtxOffset;
            }
        }

        if (_imGuiContext != IntPtr.Zero)
        {
            ImGui.DestroyContext(_imGuiContext);
            _imGuiContext = IntPtr.Zero;
        }
    }

    private struct PooledBuffer
    {
        public RenderContext? Context;
        public ulong FrameId;
        public Buffer Buffer;

        public PooledBuffer(RenderContext? context, ulong frameId, Buffer buffer)
        {
            Context = context;
            FrameId = frameId;
            Buffer = buffer;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ImGuiRootArguments
    {
        public Vector2 Scale;
        public Vector2 Translate;
        public GPUBufferView Vertices;
        public uint Texture;
        private uint _padding0;
    }
}
