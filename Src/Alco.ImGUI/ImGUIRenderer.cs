using System.Numerics;
using Alco.Graphics;
using Alco.Rendering;
using Alco;
using Alco.ImGUI;
using System.Diagnostics.CodeAnalysis;

namespace Alco.ImGUI;

/// <summary>
/// Renderer that bridges ImGui draw data to the engine rendering backend, and manages ImGui font atlas.
/// </summary>
public unsafe class ImGUIRenderer : AutoDisposable
{
    public static ImGUIRenderer? Instance { get; private set; }

    private readonly RenderingSystem _renderingSystem;
    private readonly GPUDevice _device;
    private readonly GPUCommandBuffer _commandBuffer;
    private readonly PrimitiveMesh _mesh;
    private readonly GraphicsBuffer _viewProjectionBuffer;
    private readonly Material _material;
    private IntPtr _imGuiContext;
    private GPUFrameBuffer? _target;
    private readonly uint _shaderId_Texture;
    private readonly IntPtr _fontTextureId = (IntPtr)(-1);
    private Texture2D _fontTexture;
    private NativeBuffer<byte> _tmpIndexBuffer;
    private bool _fontTextureDirty;
    private readonly HashSet<FontLanguage> _addedLanguages = new HashSet<FontLanguage>();

    private readonly List<Texture2D> _textures = new List<Texture2D>();
    private ImGuiNative.ImGuiErrorRecoveryState _recoveryState;
    private bool _recoveryStateStored;
    private bool _frameValid;

    public ImGUIRenderer(RenderingSystem renderingSystem, Material material, string name)
    {
        _renderingSystem = renderingSystem;
        _device = renderingSystem.GraphicsDevice;
        _commandBuffer = _device.CreateCommandBuffer($"{name}_command_buffer");
        _mesh = renderingSystem.CreatePrimitiveMesh((uint)sizeof(ImDrawVert) * 64, (uint)sizeof(ushort) * 96, "ImGuiRenderer_mesh");

        _viewProjectionBuffer = renderingSystem.CreateGraphicsBuffer((uint)sizeof(Matrix4x4), "ImGuiRenderer_view_projection_buffer");
        _material = material.CreateInstance();
        _material.SetBuffer(ShaderResourceId.Camera, _viewProjectionBuffer);

        _imGuiContext = ImGui.CreateContext();
        ImGui.SetCurrentContext(_imGuiContext);
        ImGuizmo.SetImGuiContext(_imGuiContext);
        ImGuiNative.InitializeErrorHandling();

        _shaderId_Texture = _material.GetResourceId(ShaderResourceId.Texture);

        ImGuiIOPtr io = ImGui.GetIO();

        io.ConfigErrorRecovery = true;
        io.ConfigErrorRecoveryEnableAssert = true;
        io.ConfigErrorRecoveryEnableDebugLog = true;
        io.ConfigErrorRecoveryEnableTooltip = true;

        io.Fonts.AddFontDefault();
        io.Fonts.Flags |= ImFontAtlasFlags.NoBakedLines;

        io.Fonts.GetTexDataAsRGBA32(out byte* pixels, out int width, out int height, out int bytesPerPixel);
        io.Fonts.SetTexID(_fontTextureId);

        ReadOnlySpan<byte> fontTextureData = new ReadOnlySpan<byte>(pixels, width * height * bytesPerPixel);
        _fontTexture = renderingSystem.CreateTexture2D(fontTextureData, (uint)width, (uint)height, ImageLoadOption.Default);

        io.Fonts.ClearTexData();
        io.Fonts.TexReady = true;

        _tmpIndexBuffer = new NativeBuffer<byte>(96);

        if (Instance != null)
        {
            throw new Exception("ImGUIRenderer can only have one instance at a time");
        }
        Instance = this;
    }



    public IntPtr AddTexture(Texture2D texture)
    {
        ArgumentNullException.ThrowIfNull(texture);
        _textures.Add(texture);
        return _textures.Count - 1;
    }

    public void Begin(GPUFrameBuffer target, float deltaTime)
    {
        uint width = target.Width;
        uint height = target.Height;
        ImGuiIOPtr io = ImGui.GetIO();

        if (_fontTextureDirty)
        {
            UpdateFontTexture();
            _fontTextureDirty = false;
        }
        io.DisplaySize = new Vector2(width, height);
        io.DisplayFramebufferScale = new Vector2(1.0f, 1.0f);
        io.DeltaTime = deltaTime;

        ImGuizmo.SetRect(0, 0, width, height);

        _frameValid = true;
        if (_recoveryStateStored)
            RecoverState();

        try
        {
            ImGui.NewFrame();
            ImGui.ErrorRecoveryStoreState(ref _recoveryState);
            _recoveryStateStored = true;
            ImGuizmo.BeginFrame();
        }
        catch (Exception e)
        {
            Log.Error("[ImGUI] Error during NewFrame: ", e);
            if (_recoveryStateStored)
                RecoverState();
            _frameValid = false;
            return;
        }

        _target = target;
    }

    public void End()
    {
        if (!_frameValid)
            return;

        try
        {
            ImGui.Render();
        }
        catch (Exception e)
        {
            RecoverState();
            Log.Error("[ImGUI] Error during Render: ", e);
            _frameValid = false;
            _target = null;
            _textures.Clear();
            return;
        }

        ImDrawDataPtr drawData = ImGui.GetDrawData();

        if (drawData.CmdListsCount <= 0)
        {
            return;
        }

        uint totalVertexBufferSize = (uint)(drawData.TotalVtxCount * sizeof(ImDrawVert));
        uint totalIndexBufferSize = (uint)(drawData.TotalIdxCount * sizeof(ushort));

        _mesh.EnsureVertexBufferSizeUnsafe(totalVertexBufferSize);
        _mesh.EnsureIndexBufferSizeUnsafe(totalIndexBufferSize);
        _tmpIndexBuffer.SetSize((int)totalIndexBufferSize);
        byte* tmpIndexBufferPtr = _tmpIndexBuffer.UnsafePointer;

        uint vertexBufferOffset = 0;
        uint indexBufferOffset = 0;


        for (int i = 0; i < drawData.CmdListsCount; i++)
        {
            ImDrawListPtr cmdList = drawData.CmdLists[i];

            void* vertexDataPtr = (void*)cmdList.VtxBuffer.Data;
            uint vertexDataSize = (uint)(cmdList.VtxBuffer.Size * sizeof(ImDrawVert));

            void* indexDataPtr = (void*)cmdList.IdxBuffer.Data;
            uint indexDataSize = (uint)(cmdList.IdxBuffer.Size * sizeof(ushort));

            _mesh.UpdateVertexUnsafe(vertexDataPtr, vertexDataSize, vertexBufferOffset);


            MemoryUtility.MemCopy(indexDataPtr, tmpIndexBufferPtr + indexBufferOffset, indexDataSize);

            vertexBufferOffset += vertexDataSize;
            indexBufferOffset += indexDataSize;
        }

        _mesh.UpdateIndicesUnsafe(_tmpIndexBuffer.UnsafePointer, totalIndexBufferSize, 0);

        var io = ImGui.GetIO();
        _viewProjectionBuffer.UpdateBuffer(Matrix4x4.CreateOrthographicOffCenter(0, io.DisplaySize.X, io.DisplaySize.Y, 0, -1, 1));

        drawData.ScaleClipRects(ImGui.GetIO().DisplayFramebufferScale);

        ShaderPipelineInfo pipelineInfo = _material.GetPipelineInfo(_target!.AttachmentLayout);

        _commandBuffer.Begin();

        using (var renderPass = _commandBuffer.BeginRender(_target))
        {
            renderPass.SetPipeline(pipelineInfo.Pipeline);
            renderPass.SetVertexBuffer(0, _mesh.VertexBuffer);
            renderPass.SetIndexBuffer(_mesh.IndexBuffer, IndexFormat.UInt16);
            _material.PushResources(renderPass);

            uint vertexOffset = 0;
            uint indexOffset = 0;

            for (int i = 0; i < drawData.CmdListsCount; i++)
            {
                ImDrawListPtr cmdList = drawData.CmdLists[i];

                for (int j = 0; j < cmdList.CmdBuffer.Size; j++)
                {
                    ImDrawCmdPtr cmd = cmdList.CmdBuffer[j];
                    nint textureId = cmd.TextureId;

                    if (TryGetTexture(textureId, out Texture2D? texture) && !texture.IsDisposed)
                    {
                        renderPass.SetResources(_shaderId_Texture, texture.EntrySample);
                    }
                    else
                    {
                        renderPass.SetResources(_shaderId_Texture, _renderingSystem.TextureWhite.EntrySample);
                    }

                    Vector4 clipRect = cmd.ClipRect;

                    renderPass.SetScissorRect(
                        (uint)clipRect.X,
                        (uint)clipRect.Y,
                        (uint)(clipRect.Z - clipRect.X),
                        (uint)(clipRect.W - clipRect.Y));


                    renderPass.DrawIndexed(cmd.ElemCount, 1, cmd.IdxOffset + indexOffset, (int)(cmd.VtxOffset + vertexOffset), 0);
                }

                indexOffset += (uint)cmdList.IdxBuffer.Size;
                vertexOffset += (uint)cmdList.VtxBuffer.Size;
            }

        }

        _commandBuffer.End();
        _device.Submit(_commandBuffer);
        _target = null;

        _textures.Clear();
    }

    /// <summary>
    /// Add a font from memory with a predefined language range. Pixel size is fixed to 16.
    /// Deduplicated by language to avoid adding the same language multiple times.
    /// </summary>
    /// <param name="fontData">Font bytes.</param>
    /// <param name="language">Predefined language range.</param>
    public void AddFontForLanguage(ReadOnlySpan<byte> fontData, FontLanguage language)
    {
        ImGuiIOPtr io = ImGui.GetIO();
        if (_addedLanguages.Contains(language))
        {
            return;
        }
        // Basic is already covered by AddFontDefault in constructor
        if (language == FontLanguage.Basic)
        {
            _addedLanguages.Add(language);
            return;
        }

        IntPtr ranges = language switch
        {
            FontLanguage.Basic => io.Fonts.GetGlyphRangesDefault(),
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
            // Merge glyphs into the default font to keep a single active font
            ImFontConfigPtr cfg = new ImFontConfigPtr(ImGuiNative.ImFontConfig_ImFontConfig());
            cfg.MergeMode = true;
            cfg.FontDataOwnedByAtlas = false;
            io.Fonts.AddFontFromMemoryTTF((IntPtr)fontDataPtr, fontData.Length, 16, cfg, ranges);
            cfg.Destroy();
        }
        _addedLanguages.Add(language);
        _fontTextureDirty = true;
    }

    private void UpdateFontTexture()
    {
        ImGuiIOPtr io = ImGui.GetIO();
        io.Fonts.GetTexDataAsRGBA32(out byte* pixels, out int width, out int height, out int bytesPerPixel);
        ReadOnlySpan<byte> fontTextureData = new ReadOnlySpan<byte>(pixels, width * height * bytesPerPixel);

        if (_fontTexture != null && !_fontTexture.IsDisposed)
        {
            _fontTexture.Dispose();
        }
        _fontTexture = _renderingSystem.CreateTexture2D(fontTextureData, (uint)width, (uint)height, ImageLoadOption.Default);

        // after uploading we can drop CPU-side font tex data and mark ready
        io.Fonts.ClearTexData();
        io.Fonts.TexReady = true;

        io.Fonts.SetTexID(_fontTextureId);
    }

    private bool TryGetTexture(IntPtr textureId, [NotNullWhen(true)] out Texture2D? texture)
    {
        texture = null;
        if (textureId == _fontTextureId)
        {
            texture = _fontTexture;
            return true;
        }

        if (textureId < _textures.Count)
        {
            texture = _textures[(int)textureId];
            return true;
        }

        return false;
    }

    private void RecoverState()
    {
        ImGuiIOPtr io = ImGui.GetIO();
        io.ConfigErrorRecoveryEnableAssert = false;
        ImGui.ErrorRecoveryTryToRecoverState(ref _recoveryState);
        io.ConfigErrorRecoveryEnableAssert = true;
    }

    protected override void Dispose(bool disposing)
    {
        Instance = null;
        if (disposing)
        {
            _commandBuffer.Dispose();
            _viewProjectionBuffer.Dispose();
            _mesh.Dispose();

        }
        _tmpIndexBuffer.Dispose();
        ImGui.DestroyContext(_imGuiContext);
    }

}

