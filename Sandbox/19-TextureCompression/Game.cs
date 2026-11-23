using System.Numerics;
using System.Runtime.InteropServices;
using Alco;
using Alco.Engine;
using Alco.Graphics;
using Alco.Graphics.LowLevel;
using Alco.ImGUI;
using Alco.IO;
using Alco.Rendering;
using Buffer = Alco.Graphics.Buffer;

/// <summary>Interactive sandbox comparing uncompressed textures against GPU BC1-BC7 compressed textures and error metrics.</summary>
public unsafe class Game : GameEngine
{
    private const string ComparisonShaderSource = """
        #language slang 2025
        module TextureCompressionComparison;

        import Core;
        import SharedSampler;

        [GPURoot]
        public struct ComparisonRoot
        {
            public uint2 SourceExtent;
            public float2 TextureExtent;
            public float2 PanelExtent;
            public float2 PanTexels;
            public float PixelsPerTexel;
            public uint Texture;
            public uint Original;
            public uint Premultiplied;
            public uint DisplayMode;
            public float ErrorGain;
            public uint PointSample;
            public uint Pad0;
        };

        struct V2F
        {
            float4 Position : SV_Position;
            float2 Uv : TEXCOORD0;
        };

        float3 CompositeChecker(float4 value, float background, uint premultiplied)
        {
            float3 color = premultiplied != 0u ? value.rgb : value.rgb * value.a;
            return saturate(color + background * (1.0f - value.a));
        }

        float4 SamplePanelTexel(Texture2D<float4> tex, float2 uv, uint pointSample)
        {
            if (pointSample != 0u)
            {
                return tex.SampleLevel(shared_sampler_nearest_clamp(), uv, 0.0f);
            }
            return tex.SampleLevel(shared_sampler_linear_clamp(), uv, 0.0f);
        }

        [shader("vertex")]
        V2F MainVS(uint vertexId : SV_VertexID)
        {
            float2 uv = float2((vertexId << 1) & 2, vertexId & 2);
            V2F output;
            output.Position = float4(uv * float2(2.0f, -2.0f) + float2(-1.0f, 1.0f), 0.0f, 1.0f);
            output.Uv = uv;
            return output;
        }

        [shader("pixel")]
        float4 MainPS(V2F input) : SV_Target0
        {
            ComparisonRoot root = gpu_load_root<ComparisonRoot>();
            float2 local = input.Uv * root.PanelExtent;
            uint2 cell = uint2(floor(local / 16.0f));
            float background = ((cell.x + cell.y) & 1u) != 0u ? 0.65f : 0.25f;
            float2 texel = (local - root.PanelExtent * 0.5f) / root.PixelsPerTexel
                + float2(root.SourceExtent) * 0.5f + root.PanTexels;

            if (any(texel < 0.0f) || any(texel >= float2(root.SourceExtent)))
            {
                return float4(background.xxx, 1.0f);
            }

            texel = clamp(texel, 0.5f, float2(root.SourceExtent) - 0.5f);
            Texture2D<float4> tex = gpu_texture<Texture2D<float4>>(root.Texture);
            float4 value = SamplePanelTexel(tex, texel / root.TextureExtent, root.PointSample);
            float3 color = CompositeChecker(value, background, root.Premultiplied);

            if (root.DisplayMode != 0u)
            {
                Texture2D<float4> original = gpu_texture<Texture2D<float4>>(root.Original);
                float4 reference = SamplePanelTexel(original, texel / float2(root.SourceExtent), root.PointSample);
                if (root.DisplayMode == 1u)
                {
                    color = abs(value.rgb - reference.rgb);
                }
                else if (root.DisplayMode == 2u)
                {
                    color = abs(value.a - reference.a).xxx;
                }
                else
                {
                    color = abs(color - CompositeChecker(reference, background, root.Premultiplied));
                }
                color = saturate(color * root.ErrorGain);
            }

            return float4(color, 1.0f);
        }
        """;

    private const string DecodeVerifySource = """
        #language slang 2025
        module SandboxTextureDecodeVerify;

        import Core;

        [GPURoot]
        public struct DecodeVerifyRoot
        {
            public GPUBufferView Output;
            public uint Source;
            public uint2 Extent;
        };

        [shader("compute")]
        [numthreads(8, 8, 1)]
        void mainCS(uint3 thread : SV_DispatchThreadID)
        {
            DecodeVerifyRoot root = gpu_load_root<DecodeVerifyRoot>();
            if (any(thread.xy >= root.Extent))
            {
                return;
            }
            Texture2D<float4> source = gpu_texture<Texture2D<float4>>(root.Source);
            uint pixel = thread.y * root.Extent.x + thread.x;
            gpu_store<float4>(root.Output, pixel, source.Load(int3(thread.xy, 0)));
        }
        """;

    [StructLayout(LayoutKind.Sequential)]
    private struct ComparisonRoot
    {
        public Extent2D SourceExtent;
        public Vector2 TextureExtent;
        public Vector2 PanelExtent;
        public Vector2 PanTexels;
        public float PixelsPerTexel;
        public uint Texture;
        public uint Original;
        public uint Premultiplied;
        public uint DisplayMode;
        public float ErrorGain;
        public uint PointSample;
        public uint Pad0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DecodeVerifyRoot
    {
        public GPUBufferView Output;
        public uint Source;
        public Extent2D Extent;
    }

    private static readonly string[] SourceFiles = ["test.png", "test.jpg"];
    private static readonly string[] FormatLabels = ["BC1", "BC2", "BC3", "BC4", "BC5", "BC7"];
    private static readonly string[] DiffModeLabels = ["RGB Diff", "Alpha Diff", "Composited Diff"];

    private readonly RenderContext _renderContext;
    private readonly TextureCompressor _compressor;
    private readonly Material _comparisonMaterial;
    private readonly Shader _decodeShader;

    private Texture? _sourceTexture;
    private Texture? _compressedTexture;
    private byte[] _sourceBytes = [];
    private int _sourceWidth;
    private int _sourceHeight;

    private int _selectedSourceIndex;
    private int _selectedFormatIndex = 5; // BC7
    private int _selectedDiffMode = 1; // RGB Diff
    private bool _srgb = true;
    private bool _premultiplyAlpha;
    private bool _pointSample = true;
    private float _zoom = 1.0f;
    private Vector2 _panTexels;
    private float _errorGain = 12.0f;

    private double _psnrRgb;
    private int _maxRgbError;
    private double _psnrAlpha;
    private int _maxAlphaError;
    private ulong _sourceMemoryBytes;
    private ulong _compressedMemoryBytes;

    /// <summary>Initializes rendering, compressor, and loads the default comparison texture.</summary>
    /// <param name="setting">Engine settings for the game instance.</param>
    public Game(GameEngineSetting setting) : base(setting)
    {
        AddSystem(new ImGUISystem(this));

        _renderContext = new RenderContext(RenderingSystem);
        _compressor = new TextureCompressor(RenderingSystem);

        Shader comparisonShader = ShaderSystem.GetShaderFromSource(
            "TextureCompressionComparison",
            "TextureCompressionComparison.slang",
            ComparisonShaderSource,
            ShaderKind.Graphics,
            "TextureCompressionComparison");
        _comparisonMaterial = comparisonShader.CreateMaterial("comparison_panel");
        _decodeShader = ShaderSystem.GetShaderFromSource(
            "SandboxTextureDecodeVerify",
            "SandboxTextureDecodeVerify.slang",
            DecodeVerifySource,
            ShaderKind.Compute,
            "SandboxTextureDecodeVerify");

        ReloadSourceAndCompress();
    }

    /// <summary>Updates camera pan/zoom, draws the 3-panel comparison view, and renders the ImGui control panel.</summary>
    /// <param name="delta">Elapsed frame duration in seconds.</param>
    protected override void OnUpdate(float delta)
    {
        if (Input.IsKeyDown(KeyCode.Escape))
        {
            Stop();
            return;
        }

        HandleViewportInput();
        DrawControls();

        if (!MainPresenter.HasValidFrame || _sourceTexture == null || _compressedTexture == null)
        {
            return;
        }

        SwapchainFrame frame = MainPresenter.CurrentFrame;
        uint totalWidth = frame.Extent.X;
        uint totalHeight = frame.Extent.Y;
        if (totalWidth < 3 || totalHeight == 0)
        {
            return;
        }

        bool begun = false;
        try
        {
            _renderContext.BeginFrame();
            begun = true;

            ColorAttachment color = new()
            {
                RenderView = frame.RenderView,
                Load = LoadOp.Clear,
                Store = StoreOp.Store,
                Clear = new Vector4(0.08f, 0.08f, 0.1f, 1.0f),
            };
            RenderingDesc pass = new() { Colors = [color] };
            using (RenderContext.RenderScope render = _renderContext.BeginRenderPass(pass, [MainPresenter.SurfaceFormat]))
            {
                uint panelWidth = totalWidth / 3;
                uint panelGap = 2;
                Extent2D sourceExtent = new((uint)_sourceWidth, (uint)_sourceHeight);
                Extent2D paddedExtent = TextureCompressor.GetPaddedExtent(sourceExtent);

                // Fit image into a single panel at zoom == 1.0.
                float baseScale = MathF.Min(
                    MathF.Max(1.0f, panelWidth - 20.0f) / _sourceWidth,
                    MathF.Max(1.0f, totalHeight - 20.0f) / _sourceHeight);
                float pixelsPerTexel = baseScale * _zoom;

                bool activeSrgb = TextureUtility.IsSrgbFormat(ResolveSelectedFormat());
                uint sourceSlot = activeSrgb
                    ? _sourceTexture.GetSampledIndex(Format.Rgba8Srgb)
                    : _sourceTexture.SampledIndex;
                uint compressedSlot = _compressedTexture.SampledIndex;
                uint premulFlag = _premultiplyAlpha ? 1u : 0u;
                uint pointFlag = _pointSample ? 1u : 0u;

                // Panel 0: Original
                DrawPanel(render, _renderContext, 0, panelWidth - panelGap, totalHeight, new ComparisonRoot
                {
                    SourceExtent = sourceExtent,
                    TextureExtent = new Vector2(_sourceWidth, _sourceHeight),
                    PanelExtent = new Vector2(panelWidth - panelGap, totalHeight),
                    PanTexels = _panTexels,
                    PixelsPerTexel = pixelsPerTexel,
                    Texture = sourceSlot,
                    Original = sourceSlot,
                    Premultiplied = premulFlag,
                    DisplayMode = 0,
                    ErrorGain = 1.0f,
                    PointSample = pointFlag,
                });

                // Panel 1: Compressed BC
                DrawPanel(render, _renderContext, panelWidth, panelWidth - panelGap, totalHeight, new ComparisonRoot
                {
                    SourceExtent = sourceExtent,
                    TextureExtent = new Vector2(paddedExtent.X, paddedExtent.Y),
                    PanelExtent = new Vector2(panelWidth - panelGap, totalHeight),
                    PanTexels = _panTexels,
                    PixelsPerTexel = pixelsPerTexel,
                    Texture = compressedSlot,
                    Original = sourceSlot,
                    Premultiplied = premulFlag,
                    DisplayMode = 0,
                    ErrorGain = 1.0f,
                    PointSample = pointFlag,
                });

                // Panel 2: Magnified Difference
                uint rightX = panelWidth * 2;
                uint rightWidth = totalWidth - rightX;
                DrawPanel(render, _renderContext, rightX, rightWidth, totalHeight, new ComparisonRoot
                {
                    SourceExtent = sourceExtent,
                    TextureExtent = new Vector2(paddedExtent.X, paddedExtent.Y),
                    PanelExtent = new Vector2(rightWidth, totalHeight),
                    PanTexels = _panTexels,
                    PixelsPerTexel = pixelsPerTexel,
                    Texture = compressedSlot,
                    Original = sourceSlot,
                    Premultiplied = premulFlag,
                    DisplayMode = (uint)_selectedDiffMode,
                    ErrorGain = _errorGain,
                    PointSample = pointFlag,
                });
            }

            MainPresenter.EnqueueFrameContext(_renderContext);
        }
        catch
        {
            if (begun)
            {
                _renderContext.DiscardFrame();
            }
            throw;
        }
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        if (IsDisposed)
        {
            return;
        }

        GraphicsDevice.WaitIdle();
        _compressedTexture?.Destroy();
        _compressedTexture = null;
        _sourceTexture?.Destroy();
        _sourceTexture = null;
        _renderContext.Destroy();
        base.Dispose();
    }

    private void DrawPanel(
        RenderContext.RenderScope render,
        RenderContext context,
        uint x,
        uint width,
        uint height,
        in ComparisonRoot root)
    {
        if (width == 0 || height == 0)
            return;

        render.SetViewport(new Viewport { X = x, Y = 0, Width = width, Height = height });
        render.SetScissor(new Scissor { X = (int)x, Y = 0, Width = width, Height = height });
        GPURange rootRange = context.AllocateRoot(root);
        render.Draw(_comparisonMaterial, rootRange, DepthStencilState.None, 3);
    }

    private void HandleViewportInput()
    {
        if (ImGui.GetIO().WantCaptureMouse)
        {
            return;
        }

        if (Input.IsMouseScrolling(out Vector2 wheel) && MathF.Abs(wheel.Y) > 0.001f)
        {
            _zoom = Math.Clamp(_zoom * MathF.Pow(1.15f, wheel.Y), 0.25f, 32.0f);
        }

        if (Input.IsMouseDown(Mouse.Left))
        {
            uint panelWidth = Math.Max(1u, MainView.Size.X / 3u);
            float baseScale = MathF.Min(
                MathF.Max(1.0f, panelWidth - 20.0f) / Math.Max(1, _sourceWidth),
                MathF.Max(1.0f, MainView.Size.Y - 20.0f) / Math.Max(1, _sourceHeight));
            float pixelsPerTexel = MathF.Max(0.01f, baseScale * _zoom);
            _panTexels -= Input.MouseDelta / pixelsPerTexel;
        }
    }

    private void DrawControls()
    {
        ImGui.Begin("Texture Compression");
        ImGui.Text("Panels: Left = Original | Middle = Compressed | Right = Error");

        bool sourceChanged = false;
        if (ImGui.Combo("Source Asset", ref _selectedSourceIndex, SourceFiles, SourceFiles.Length))
        {
            sourceChanged = true;
        }
        if (ImGui.Checkbox("Premultiply Alpha", ref _premultiplyAlpha))
        {
            sourceChanged = true;
        }

        bool recompressNeeded = sourceChanged;
        if (ImGui.Combo("BC Format", ref _selectedFormatIndex, FormatLabels, FormatLabels.Length))
        {
            recompressNeeded = true;
        }
        if (ImGui.Checkbox("sRGB View", ref _srgb))
        {
            recompressNeeded = true;
        }

        if (sourceChanged)
        {
            ReloadSourceAndCompress();
        }
        else if (recompressNeeded)
        {
            CompressCurrentSource();
        }

        ImGui.Separator();
        ImGui.Checkbox("Point Sample", ref _pointSample);
        int diffIdx = _selectedDiffMode - 1;
        if (ImGui.Combo("Error Channel", ref diffIdx, DiffModeLabels, DiffModeLabels.Length))
        {
            _selectedDiffMode = diffIdx + 1;
        }
        ImGui.SliderFloat("Error Gain", ref _errorGain, 1.0f, 32.0f);
        ImGui.SliderFloat("Zoom", ref _zoom, 0.25f, 32.0f);
        if (ImGui.Button("Reset View"))
        {
            _zoom = 1.0f;
            _panTexels = Vector2.Zero;
        }

        ImGui.Separator();
        Extent2D padded = TextureCompressor.GetPaddedExtent(new Extent2D((uint)_sourceWidth, (uint)_sourceHeight));
        ImGui.Text($"Source: {_sourceWidth}x{_sourceHeight} (Padded: {padded.X}x{padded.Y})");
        double ratio = _compressedMemoryBytes > 0 ? (double)_sourceMemoryBytes / _compressedMemoryBytes : 0.0;
        ImGui.Text($"Size: {_sourceMemoryBytes / 1024.0:F1} KB -> {_compressedMemoryBytes / 1024.0:F1} KB ({ratio:F1}x)");
        ImGui.Text($"RGB PSNR: {(_psnrRgb == double.PositiveInfinity ? "inf" : $"{_psnrRgb:F2} dB")} (Max Err: {_maxRgbError})");
        ImGui.Text($"Alpha PSNR: {(_psnrAlpha == double.PositiveInfinity ? "inf" : $"{_psnrAlpha:F2} dB")} (Max Err: {_maxAlphaError})");

        ImGui.End();
    }

    private void ReloadSourceAndCompress()
    {
        string assetName = SourceFiles[_selectedSourceIndex];
        if (!AssetSystem.TryLoadRaw(assetName, out SafeMemoryHandle handle))
        {
            throw new FileNotFoundException($"Sandbox asset '{assetName}' not found.");
        }

        byte* decodedPixels = null;
        try
        {
            ReadOnlySpan<byte> fileBytes = handle.AsSpan();
            decodedPixels = string.Equals(Path.GetExtension(assetName), ".png", StringComparison.OrdinalIgnoreCase)
                ? ImageDecodeUtility.DecodePng(fileBytes, out _sourceWidth, out _sourceHeight)
                : ImageDecodeUtility.DecodeJpeg(fileBytes, out _sourceWidth, out _sourceHeight);

            _sourceBytes = new byte[checked(_sourceWidth * _sourceHeight * 4)];
            fixed (byte* dst = _sourceBytes)
            {
                System.Buffer.MemoryCopy(decodedPixels, dst, _sourceBytes.Length, _sourceBytes.Length);
            }
        }
        finally
        {
            if (decodedPixels != null)
            {
                NativeMemory.Free(decodedPixels);
            }
            handle.Dispose();
        }

        if (_premultiplyAlpha)
        {
            ImageUtility.PremultiplyRgba8(_sourceBytes);
        }

        GraphicsDevice.WaitIdle();
        _sourceTexture?.Destroy();
        _sourceTexture = RenderingSystem.CreateTexture(new TextureDesc
        {
            Extent = new Extent3D((uint)_sourceWidth, (uint)_sourceHeight, 1),
            Format = Format.Rgba8Unorm,
            Usage = TextureUsage.Sampled | TextureUsage.TransferDestination,
            MutableFormat = true,
        });
        RenderingSystem.UploadTexture(_sourceTexture, _sourceBytes);
        _sourceMemoryBytes = (ulong)_sourceBytes.Length;

        CompressCurrentSource();
    }

    private void CompressCurrentSource()
    {
        if (_sourceTexture == null)
            return;

        Format targetFormat = ResolveSelectedFormat();
        if (!TextureCompressor.CanCompressOnGpu(GraphicsDevice, targetFormat))
            return;

        GraphicsDevice.WaitIdle();
        _compressedTexture?.Destroy();
        _compressedTexture = _compressor.CompressToTexture(
            _sourceTexture,
            targetFormat,
            includeMips: false,
            inputColorSpace: CompressionInputColorSpace.EncodedCodeDomain,
            mutableFormat: true);

        Extent2D logical = new((uint)_sourceWidth, (uint)_sourceHeight);
        _compressedMemoryBytes = TextureCompressor.GetCompressedSizeBytes(targetFormat, logical);
        MeasureQualityMetrics(targetFormat);
    }

    private void MeasureQualityMetrics(Format encodedFormat)
    {
        if (_compressedTexture == null)
            return;

        uint texelCount = (uint)(_sourceWidth * _sourceHeight);
        Format unormViewFormat = ToUnorm(encodedFormat);
        uint decodeSlot = _compressedTexture.GetSampledIndex(unormViewFormat);
        Buffer decodeBuffer = RenderingSystem.CreateBuffer(texelCount * (ulong)sizeof(Vector4), MemoryType.GpuOnly);
        Vector4[] decoded = new Vector4[texelCount];
        try
        {
            _renderContext.BeginFrame();
            using (RenderContext.ComputeScope compute = _renderContext.BeginCompute())
            {
                GPURange root = _renderContext.AllocateRoot(new DecodeVerifyRoot
                {
                    Output = decodeBuffer.View,
                    Source = decodeSlot,
                    Extent = new Extent2D((uint)_sourceWidth, (uint)_sourceHeight),
                });
                compute.Dispatch(_decodeShader, root, new Extent3D(((uint)_sourceWidth + 7u) / 8u, ((uint)_sourceHeight + 7u) / 8u, 1u));
            }
            RenderingSystem.Submit([_renderContext]).Wait();
            RenderingSystem.ReadBuffer(decodeBuffer, MemoryMarshal.AsBytes(decoded.AsSpan()));
        }
        finally
        {
            decodeBuffer.Destroy();
        }

        double rgbSqErr = 0.0;
        double alphaSqErr = 0.0;
        int maxRgb = 0;
        int maxAlpha = 0;
        int rgbCount = 0;
        int alphaCount = 0;

        for (int i = 0; i < (int)texelCount; ++i)
        {
            Vector4 d = decoded[i];
            int srcOffset = i * 4;
            int rErr = Math.Abs(Math.Clamp((int)MathF.Round(d.X * 255.0f), 0, 255) - _sourceBytes[srcOffset + 0]);
            maxRgb = Math.Max(maxRgb, rErr);
            rgbSqErr += rErr * rErr;
            rgbCount++;

            if (encodedFormat != Format.Bc4Unorm && encodedFormat != Format.Bc4Snorm)
            {
                int gErr = Math.Abs(Math.Clamp((int)MathF.Round(d.Y * 255.0f), 0, 255) - _sourceBytes[srcOffset + 1]);
                maxRgb = Math.Max(maxRgb, gErr);
                rgbSqErr += gErr * gErr;
                rgbCount++;
            }

            if (encodedFormat is not (Format.Bc4Unorm or Format.Bc4Snorm or Format.Bc5Rg))
            {
                int bErr = Math.Abs(Math.Clamp((int)MathF.Round(d.Z * 255.0f), 0, 255) - _sourceBytes[srcOffset + 2]);
                maxRgb = Math.Max(maxRgb, bErr);
                rgbSqErr += bErr * bErr;
                rgbCount++;
            }

            if (encodedFormat is Format.Bc1Unorm or Format.Bc1Srgb or Format.Bc2Unorm or Format.Bc2Srgb or Format.Bc3Unorm or Format.Bc3Srgb or Format.Bc7Unorm or Format.Bc7Srgb)
            {
                int aErr = Math.Abs(Math.Clamp((int)MathF.Round(d.W * 255.0f), 0, 255) - _sourceBytes[srcOffset + 3]);
                maxAlpha = Math.Max(maxAlpha, aErr);
                alphaSqErr += aErr * aErr;
                alphaCount++;
            }
        }

        _maxRgbError = maxRgb;
        _maxAlphaError = maxAlpha;
        double rgbMse = rgbCount > 0 ? rgbSqErr / rgbCount : 0.0;
        double alphaMse = alphaCount > 0 ? alphaSqErr / alphaCount : 0.0;
        _psnrRgb = rgbMse == 0.0 ? double.PositiveInfinity : 10.0 * Math.Log10(255.0 * 255.0 / rgbMse);
        _psnrAlpha = alphaMse == 0.0 ? double.PositiveInfinity : 10.0 * Math.Log10(255.0 * 255.0 / alphaMse);
    }

    private Format ResolveSelectedFormat() => (_selectedFormatIndex, _srgb) switch
    {
        (0, true) => Format.Bc1Srgb,
        (0, false) => Format.Bc1Unorm,
        (1, true) => Format.Bc2Srgb,
        (1, false) => Format.Bc2Unorm,
        (2, true) => Format.Bc3Srgb,
        (2, false) => Format.Bc3Unorm,
        (3, _) => Format.Bc4Unorm,
        (4, _) => Format.Bc5Rg,
        (5, true) => Format.Bc7Srgb,
        _ => Format.Bc7Unorm,
    };

    private static Format ToUnorm(Format format) => format switch
    {
        Format.Bc1Srgb => Format.Bc1Unorm,
        Format.Bc2Srgb => Format.Bc2Unorm,
        Format.Bc3Srgb => Format.Bc3Unorm,
        Format.Bc7Srgb => Format.Bc7Unorm,
        _ => format,
    };
}
