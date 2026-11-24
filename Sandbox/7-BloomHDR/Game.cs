using System.Numerics;
using System.Runtime.InteropServices;
using Alco;
using Alco.Engine;
using Alco.Graphics;
using Alco.Graphics.LowLevel;
using Alco.ImGUI;
using Alco.Rendering;

/// <summary>
/// Sandbox demonstrating <see cref="RGNode_Bloom"/> and <see cref="RGNode_Tonemap"/> over the bindless RenderGraph pipeline.
/// </summary>
public sealed unsafe class Game : GameEngine
{
    [StructLayout(LayoutKind.Sequential)]
    private struct HdrQuadRoot
    {
        public Matrix4x4 ViewProjection;
        public Vector4 Color;
        public Vector2 HalfExtent;
        public Vector2 Pad;
    }

    private const string HdrQuadShaderSource = """
        #language slang 2025
        module BloomSandboxHdrQuad;

        import Core;

        [GPURoot]
        public struct HdrQuadRoot
        {
            public float4x4 ViewProjection;
            public float4 Color;
            public float2 HalfExtent;
            public float2 Pad;
        };

        struct V2F
        {
            float4 Position : SV_Position;
        };

        [shader("vertex")]
        V2F MainVS(uint vertexId : SV_VertexID)
        {
            static const float2 corners[6] = {
                float2(-1.0f, -1.0f),
                float2( 1.0f, -1.0f),
                float2( 1.0f,  1.0f),
                float2(-1.0f, -1.0f),
                float2( 1.0f,  1.0f),
                float2(-1.0f,  1.0f),
            };
            HdrQuadRoot root = gpu_load_root<HdrQuadRoot>();
            float2 world = corners[vertexId] * root.HalfExtent;
            V2F output;
            output.Position = mul(float4(world, 0.0f, 1.0f), root.ViewProjection);
            return output;
        }

        [shader("pixel")]
        float4 MainPS(V2F input) : SV_Target0
        {
            HdrQuadRoot root = gpu_load_root<HdrQuadRoot>();
            return root.Color;
        }
        """;

    private readonly RenderGraph _renderGraph;
    private readonly RenderContext _renderContext;
    private readonly RenderChain _chain = new();
    private readonly Camera2D _camera;
    private readonly Material _hdrQuadMaterial;
    private readonly RGNode_Bloom _bloomNode;
    private readonly RGNode_Tonemap _tonemapNode;

    private ColorFloat _color = new(4f, 2f, 2f, 1f);
    private bool _quadEnabled = true;

    private readonly string? _screenshotPath;
    private readonly int _screenshotFrames;
    private int _frameCount;
    private SwapchainCaptureSystem? _swapchainCapture;
    private Task<RenderCaptureResult>? _screenshotRequest;
    private bool _screenshotSaved;

    /// <summary>Initializes the bloom/HDR RenderGraph pipeline and scene state.</summary>
    /// <param name="setting">Engine and view settings.</param>
    /// <param name="args">Command-line arguments supporting optional <c>--screenshot</c> and <c>--frames</c>.</param>
    public Game(GameEngineSetting setting, string[] args) : base(setting)
    {
        _screenshotPath = GetArgValue(args, "--screenshot=");
        _screenshotFrames = int.TryParse(GetArgValue(args, "--frames="), out int frames) ? frames : 60;
        if (_screenshotPath != null)
        {
            _swapchainCapture = SwapchainCapture;
        }

        _camera = new Camera2D
        {
            Size = new Vector2(640f, 360f),
            Near = -100f,
            Far = 100f,
        };

        Shader hdrQuadShader = RenderingSystem.Shaders.GetShaderFromSource(
            "BloomSandboxHdrQuad", "BloomSandboxHdrQuad.slang", HdrQuadShaderSource, ShaderKind.Graphics);
        _hdrQuadMaterial = hdrQuadShader.CreateMaterial("hdr_quad");
        _hdrQuadMaterial.Rasterization = RasterizationState.CullNone;
        _hdrQuadMaterial.Blend = BlendState.Opaque;

        _renderGraph = new RenderGraph(RenderingSystem, new Extent2D(MainView.Size.X, MainView.Size.Y), "BloomHDRSandbox");
        _renderContext = new RenderContext(RenderingSystem, frameDataByteCount: 512 * 1024);

        _bloomNode = new RGNode_Bloom(RenderingSystem, _chain);
        _tonemapNode = new RGNode_Tonemap(RenderingSystem, _ => _chain.Current, b => b.Output)
        {
            Operator = TonemapType.Neutral,
        };
        _renderGraph.Use(new HdrSceneNode(this));
        _renderGraph.Use(_bloomNode);
        _renderGraph.Use(_tonemapNode);
    }

    /// <inheritdoc />
    protected override void OnStart()
    {
        AddSystem(new ImGUISystem(this));
    }

    /// <inheritdoc />
    protected override void OnUpdate(float delta)
    {
        if (Input.IsKeyDown(KeyCode.Escape))
        {
            Stop();
        }

        DrawControls();

        _frameCount++;
        if (_screenshotPath != null && _screenshotRequest == null && !_screenshotSaved && _frameCount >= _screenshotFrames)
        {
            _screenshotRequest = _swapchainCapture!.RequestCaptureAsync();
        }

        if (MainPresenter.HasValidFrame)
        {
            SwapchainFrame frame = MainPresenter.CurrentFrame;
            _renderGraph.Resize(frame.Extent);

            bool begun = false;
            try
            {
                _renderContext.BeginFrame();
                begun = true;
                _renderGraph.Execute(
                    _renderContext,
                    RenderGraphTarget.FromSwapchain(frame, MainPresenter.SurfaceFormat),
                    delta,
                    outputFinalUsage: RGUsage.ColorAttachment);
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

        ProcessScreenshotCapture();
    }

    /// <inheritdoc />
    protected override void OnStop()
    {
        GraphicsDevice.WaitIdle();
        _renderGraph.Destroy();
        _renderContext.Destroy();
    }

    private void DrawControls()
    {
        ImGui.SetNextWindowSize(new Vector2(370, 460), ImGuiCond.Appearing);
        ImGui.Begin("Bloom HDR Controls");

        ImGui.ColorEdit4("Color", ref _color, ImGuiColorEditFlags.HDR);
        ImGui.Checkbox("Enabled", ref _quadEnabled);

        ImGui.Separator();
        ImGui.Text("Bloom System Controls");

        bool bloomEnabled = _bloomNode.IsEnabled;
        if (ImGui.Checkbox("Bloom Enabled", ref bloomEnabled))
        {
            _bloomNode.IsEnabled = bloomEnabled;
        }

        float threshold = _bloomNode.Threshold;
        if (ImGui.SliderFloat("Bloom Threshold", ref threshold, -1.0f, 5.0f))
        {
            _bloomNode.Threshold = threshold;
        }

        float bloomIntensity = _bloomNode.Intensity;
        if (ImGui.SliderFloat("Bloom Intensity", ref bloomIntensity, 0.0f, 3.0f))
        {
            _bloomNode.Intensity = bloomIntensity;
        }

        float sizeScale = _bloomNode.SizeScale;
        if (ImGui.SliderFloat("Bloom Size Scale", ref sizeScale, 0.1f, 3.0f))
        {
            _bloomNode.SizeScale = sizeScale;
        }

        BloomCompositeMode compositeMode = _bloomNode.CompositeMode;
        if (ImGui.Combo("Composite Mode", ref compositeMode))
        {
            _bloomNode.CompositeMode = compositeMode;
        }

        RenderGraphStatistics stats = _renderGraph.Statistics;
        ImGui.Text($"RG Passes: {stats.AlivePassCount}/{stats.TotalPassCount} (culled {stats.CulledPassCount})");
        ImGui.Text($"RG Textures: {stats.ActivePhysicalTextureCount} physical / {stats.LogicalTransientTextureCount} logical");

        ImGui.Separator();
        ImGui.Text("Tone Mapping");

        TonemapType tonemapOperator = _tonemapNode.Operator;
        if (ImGui.Combo("Tone Map Type", ref tonemapOperator))
        {
            _tonemapNode.Operator = tonemapOperator;
        }

        bool ditherEnabled = _tonemapNode.DitherEnabled;
        if (ImGui.Checkbox("8-bit Dither", ref ditherEnabled))
        {
            _tonemapNode.DitherEnabled = ditherEnabled;
        }

        switch (tonemapOperator)
        {
            case TonemapType.ACES:
            {
                ACESTonemapData aces = _tonemapNode.ACESData;
                bool changed = ImGui.SliderFloat("Exposure", ref aces.Exposure, 0.1f, 4.0f);
                changed |= ImGui.SliderFloat("Gamma", ref aces.Gamma, 0.5f, 3.0f);
                if (changed)
                {
                    _tonemapNode.ACESData = aces;
                }
                break;
            }
            case TonemapType.Neutral:
            {
                NeutralTonemapData neutral = _tonemapNode.NeutralData;
                bool changed = ImGui.SliderFloat("Exposure", ref neutral.Exposure, 0.1f, 4.0f);
                changed |= ImGui.SliderFloat("Gamma", ref neutral.Gamma, 0.5f, 3.0f);
                changed |= ImGui.SliderFloat("StartCompression", ref neutral.StartCompression, 0.5f, 1.0f);
                changed |= ImGui.SliderFloat("Desaturation", ref neutral.Desaturation, 0.0f, 4.0f);
                if (changed)
                {
                    _tonemapNode.NeutralData = neutral;
                }
                break;
            }
        }

        ImGui.End();
    }

    private void ProcessScreenshotCapture()
    {
        if (_screenshotRequest is { IsCompletedSuccessfully: true })
        {
            RenderCaptureResult result = _screenshotRequest.Result;
            if (result.Success && result.PngBytes != null)
            {
                string? dir = Path.GetDirectoryName(_screenshotPath!);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                File.WriteAllBytes(_screenshotPath!, result.PngBytes);
                Console.WriteLine($"Screenshot saved to {_screenshotPath}");
            }
            else
            {
                Console.WriteLine($"Screenshot failed: {result.Error}");
            }

            _screenshotSaved = true;
            Stop();
        }
        else if (_screenshotRequest is { IsFaulted: true })
        {
            Console.WriteLine($"Screenshot failed: {_screenshotRequest.Exception?.GetBaseException().Message}");
            Stop();
        }
    }

    private static string? GetArgValue(string[] args, string prefix)
    {
        for (int i = 0; i < args.Length; ++i)
        {
            if (args[i].StartsWith(prefix, StringComparison.Ordinal))
            {
                return args[i][prefix.Length..];
            }
        }
        return null;
    }

    private sealed class HdrSceneNode(Game game) : RenderGraphNode("HdrScene")
    {
        private RGTexture _hdrTarget;

        protected override void SetupPass(RenderGraphBuilder builder, RenderGraphPassBuilder pass)
        {
            _hdrTarget = builder.CreateTexture(new RGTextureDesc
            {
                Name = "HdrSceneColor",
                Format = Format.Rgba16Float,
            });
            pass.Write(_hdrTarget, RGUsage.ColorAttachmentWrite);
            game._chain.Reset(_hdrTarget);
        }

        protected override void ExecutePass(in RenderGraphContext context)
        {
            Extent3D extent = context.GetExtent(_hdrTarget);
            ColorAttachment clearAttachment = new()
            {
                RenderView = context.GetRenderView(_hdrTarget),
                Load = LoadOp.Clear,
                Store = StoreOp.Store,
                Clear = new Vector4(0.0f, 0.0f, 0.0f, 1.0f),
            };
            RenderingDesc passDesc = new()
            {
                Colors = MemoryMarshal.CreateReadOnlySpan(ref clearAttachment, 1),
            };

            using RenderContext.RenderScope render = context.RenderContext.BeginRenderPass(passDesc);
            if (!game._quadEnabled)
            {
                return;
            }

            render.SetViewport(new Viewport { Width = extent.X, Height = extent.Y });
            render.SetScissor(new Scissor { Width = extent.X, Height = extent.Y });

            GPUAllocation<HdrQuadRoot> alloc = context.RenderContext.FrameData.Allocate<HdrQuadRoot>();
            *alloc.Cpu = new HdrQuadRoot
            {
                ViewProjection = game._camera.ViewProjectionMatrix,
                Color = new Vector4(game._color.R, game._color.G, game._color.B, game._color.A),
                HalfExtent = new Vector2(48.0f, 48.0f),
            };
            render.Draw(game._hdrQuadMaterial, alloc.GPURange, DepthStencilState.None, 6u);
        }
    }
}
