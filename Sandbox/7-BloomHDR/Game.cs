using System.Numerics;
using System.Runtime.InteropServices;
using Alco;
using Alco.Engine;
using Alco.Graphics;
using Alco.Graphics.LowLevel;
using Alco.ImGUI;
using Alco.Rendering;

/// <summary>
/// Sandbox demonstrating <see cref="RGNode_Bloom"/> and HDR tone mapping over the bindless RenderGraph pipeline.
/// </summary>
public sealed unsafe class Game : GameEngine
{
    private enum TonemapOperator
    {
        Reinhard,
        Uncharted2,
        Filmic,
        ACES,
        Neutral,
        AgX,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HdrQuadRoot
    {
        public Matrix4x4 ViewProjection;
        public Vector4 Color;
        public Vector2 HalfExtent;
        public Vector2 Pad;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TonemapRoot
    {
        public uint Source;
        public int Operator;
        public float P0;
        public float P1;
        public float P2;
        public float P3;
        public float P4;
        public float P5;
        public float P6;
        public float P7;
        public float P8;
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

    private const string TonemapShaderSource = """
        #language slang 2025
        module BloomSandboxTonemap;

        import Core;
        import SharedSampler;

        [GPURoot]
        public struct TonemapRoot
        {
            public GpuTextureId Source;
            public int Operator;
            public float P0;
            public float P1;
            public float P2;
            public float P3;
            public float P4;
            public float P5;
            public float P6;
            public float P7;
            public float P8;
        };

        struct V2F
        {
            float4 Position : SV_Position;
            float2 Uv : TEXCOORD0;
        };

        [shader("vertex")]
        V2F MainVS(uint vertexId : SV_VertexID)
        {
            float2 uv = float2((vertexId << 1) & 2, vertexId & 2);
            V2F output;
            output.Position = float4(uv * float2(2.0f, -2.0f) + float2(-1.0f, 1.0f), 0.0f, 1.0f);
            output.Uv = uv;
            return output;
        }

        float3 uncharted2Curve(float3 x, TonemapRoot r)
        {
            return ((x * (r.P0 * x + r.P2 * r.P1) + r.P3 * r.P4) /
                    (x * (r.P0 * x + r.P1) + r.P3 * r.P5)) - (r.P4 / r.P5);
        }

        float3 agxDefaultContrastApprox(float3 x)
        {
            float3 x2 = x * x;
            float3 x4 = x2 * x2;
            return + 15.5f    * x4 * x2
                   - 40.14f   * x4 * x
                   + 31.96f   * x4
                   - 6.868f   * x2 * x
                   + 0.4298f  * x2
                   + 0.1191f  * x
                   - 0.00232f;
        }

        float3 applyAgx(float3 val, float look)
        {
            const float3x3 agxMat = float3x3(
                0.842479062253094f, 0.0423282422610123f, 0.0423756549057051f,
                0.0784335999999992f, 0.878468636469772f, 0.0784336f,
                0.0792237451477643f, 0.0791661274605434f, 0.879142973793104f);
            const float3x3 agxMatInv = float3x3(
                1.19687900512017f, -0.0528968517574562f, -0.0529716355144438f,
                -0.0980208811401368f, 1.15190312990417f, -0.0980434501171241f,
                -0.0990297440797205f, -0.0989611768448433f, 1.15107367264116f);
            const float minEv = -12.47393f;
            const float maxEv = 4.026069f;

            val = max(mul(val, agxMat), 1e-6f);
            val = clamp(log2(val), minEv, maxEv);
            val = (val - minEv) / (maxEv - minEv);
            val = agxDefaultContrastApprox(val);

            const float luma = dot(val, float3(0.2126f, 0.7152f, 0.0722f));
            float3 slope = float3(1.0f, 1.0f, 1.0f);
            float3 power = float3(1.0f, 1.0f, 1.0f);
            float sat = 1.0f;
            if (look >= 0.5f && look < 1.5f)
            {
                slope = float3(1.0f, 0.9f, 0.5f);
                power = float3(0.8f, 0.8f, 0.8f);
                sat = 0.8f;
            }
            else if (look >= 1.5f)
            {
                power = float3(1.35f, 1.35f, 1.35f);
                sat = 1.4f;
            }
            val = pow(max(val * slope, 0.0f), power);
            val = luma + sat * (val - luma);
            return mul(val, agxMatInv);
        }

        float3 applyNeutral(float3 color, float startCompression, float desaturation)
        {
            float x = min(color.r, min(color.g, color.b));
            float offset = (x < 0.08f) ? (x - 6.25f * x * x) : 0.04f;
            color -= offset;
            float peak = max(color.r, max(color.g, color.b));
            if (peak < startCompression)
            {
                return color;
            }
            float d = 1.0f - startCompression;
            float newPeak = 1.0f - (d * d) / (peak + d - startCompression);
            color *= (newPeak / peak);
            float g = 1.0f - 1.0f / (desaturation * (peak - newPeak) + 1.0f);
            return lerp(color, float3(newPeak, newPeak, newPeak), g);
        }

        float outputDither8Bit(float2 pixelPos)
        {
            const float2 k = float2(0.06711056f, 0.00583715f);
            float r1 = frac(52.9829189f * frac(dot(pixelPos, k)));
            float r2 = frac(52.9829189f * frac(dot(pixelPos + 0.5f, k)));
            return (r1 + r2 - 1.0f) * (1.0f / 255.0f);
        }

        [shader("pixel")]
        float4 MainPS(V2F input) : SV_Target0
        {
            TonemapRoot root = gpu_load_root<TonemapRoot>();
            Texture2D<float4> src = gpu_texture<Texture2D<float4>>(root.Source.index);
            float3 hdr = max(src.SampleLevel(shared_sampler_linear_clamp(), input.Uv, 0.0f).rgb, 0.0f);

            float3 ldr = hdr;
            float gamma = 1.0f;
            if (root.Operator == 0)
            {
                float maxLum = max(root.P0, 1e-4f);
                gamma = max(root.P1, 1e-3f);
                float oldLum = dot(hdr, float3(0.2126f, 0.7152f, 0.0722f));
                float newLum = (oldLum * (1.0f + oldLum / (maxLum * maxLum))) / (1.0f + oldLum);
                ldr = oldLum > 1e-6f ? hdr * (newLum / oldLum) : float3(0.0f, 0.0f, 0.0f);
            }
            else if (root.Operator == 1)
            {
                gamma = max(root.P8, 1e-3f);
                float3 mapped = uncharted2Curve(hdr * root.P7, root);
                float3 whiteScale = 1.0f / uncharted2Curve(float3(root.P6, root.P6, root.P6), root);
                ldr = mapped * whiteScale;
            }
            else if (root.Operator == 2)
            {
                gamma = max(root.P1, 1e-3f);
                float3 xc = max(float3(0.0f, 0.0f, 0.0f), hdr * root.P0 - 0.004f);
                ldr = (xc * (6.2f * xc + 0.5f)) / (xc * (6.2f * xc + 1.7f) + 0.06f);
            }
            else if (root.Operator == 3)
            {
                gamma = max(root.P6, 1e-3f);
                float3 x = hdr * root.P5;
                ldr = clamp((x * (root.P0 * x + root.P1)) / (x * (root.P2 * x + root.P3) + root.P4), 0.0f, 1.0f);
            }
            else if (root.Operator == 4)
            {
                gamma = max(root.P1, 1e-3f);
                ldr = applyNeutral(hdr * root.P0, root.P2, root.P3);
            }
            else
            {
                gamma = max(root.P1, 1e-3f);
                ldr = applyAgx(hdr * root.P0, root.P2);
            }

            ldr = pow(max(ldr, 0.0f), 1.0f / gamma);
            ldr += outputDither8Bit(input.Position.xy);
            return float4(ldr, 1.0f);
        }
        """;

    private readonly RenderGraph _renderGraph;
    private readonly RenderContext _renderContext;
    private readonly RenderChain _chain = new();
    private readonly Camera2D _camera;
    private readonly Material _hdrQuadMaterial;
    private readonly Material _tonemapMaterial;
    private readonly RGNode_Bloom _bloomNode;

    private ColorFloat _color = new(4f, 2f, 2f, 1f);
    private bool _quadEnabled = true;

    private TonemapOperator _tonemapOperator = TonemapOperator.Neutral;
    private ReinhardTonemapData _reinhardData = ReinhardTonemapData.Default;
    private Uncharted2TonemapData _uncharted2Data = Uncharted2TonemapData.Default;
    private FilmicTonemapData _filmicData = FilmicTonemapData.Default;
    private ACESTonemapData _acesData = ACESTonemapData.Default;
    private NeutralTonemapData _neutralData = NeutralTonemapData.Default;
    private AgXTonemapData _agxData = AgXTonemapData.Default;

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

        Shader hdrQuadShader = ShaderSystem.GetShaderFromSource(
            "BloomSandboxHdrQuad", "BloomSandboxHdrQuad.slang", HdrQuadShaderSource, ShaderKind.Graphics);
        _hdrQuadMaterial = hdrQuadShader.CreateMaterial("hdr_quad");
        _hdrQuadMaterial.Rasterization = RasterizationState.CullNone;
        _hdrQuadMaterial.Blend = BlendState.Opaque;

        Shader tonemapShader = ShaderSystem.GetShaderFromSource(
            "BloomSandboxTonemap", "BloomSandboxTonemap.slang", TonemapShaderSource, ShaderKind.Graphics);
        _tonemapMaterial = tonemapShader.CreateMaterial("tonemap_output");
        _tonemapMaterial.Rasterization = RasterizationState.CullNone;
        _tonemapMaterial.Blend = BlendState.Opaque;

        _renderGraph = new RenderGraph(RenderingSystem, new Extent2D(MainView.Size.X, MainView.Size.Y), "BloomHDRSandbox");
        _renderContext = new RenderContext(RenderingSystem, frameDataByteCount: 512 * 1024);

        _bloomNode = new RGNode_Bloom(RenderingSystem, _chain, ShaderSystem);
        _renderGraph.Use(new HdrSceneNode(this));
        _renderGraph.Use(_bloomNode);
        _renderGraph.Use(new TonemapOutputNode(this));
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
        ImGui.Combo("Tone Map Type", ref _tonemapOperator);

        switch (_tonemapOperator)
        {
            case TonemapOperator.Reinhard:
                ImGui.SliderFloat("Max Luminance", ref _reinhardData.MaxLuminance, 0.1f, 10.0f);
                ImGui.SliderFloat("Gamma", ref _reinhardData.Gamma, 0.5f, 3.0f);
                break;
            case TonemapOperator.Uncharted2:
                ImGui.SliderFloat("Exposure", ref _uncharted2Data.Exposure, 0.1f, 4.0f);
                ImGui.SliderFloat("Gamma", ref _uncharted2Data.Gamma, 0.5f, 3.0f);
                break;
            case TonemapOperator.Filmic:
                ImGui.SliderFloat("Exposure", ref _filmicData.Exposure, 0.1f, 4.0f);
                ImGui.SliderFloat("Gamma", ref _filmicData.Gamma, 0.5f, 3.0f);
                break;
            case TonemapOperator.ACES:
                ImGui.SliderFloat("Exposure", ref _acesData.Exposure, 0.1f, 4.0f);
                ImGui.SliderFloat("Gamma", ref _acesData.Gamma, 0.5f, 3.0f);
                break;
            case TonemapOperator.Neutral:
                ImGui.SliderFloat("Exposure", ref _neutralData.Exposure, 0.1f, 4.0f);
                ImGui.SliderFloat("Gamma", ref _neutralData.Gamma, 0.5f, 3.0f);
                ImGui.SliderFloat("StartCompression", ref _neutralData.StartCompression, 0.5f, 1.0f);
                ImGui.SliderFloat("Desaturation", ref _neutralData.Desaturation, 0.0f, 4.0f);
                break;
            case TonemapOperator.AgX:
                ImGui.SliderFloat("Exposure", ref _agxData.Exposure, 0.1f, 4.0f);
                ImGui.SliderFloat("Gamma", ref _agxData.Gamma, 0.5f, 3.0f);
                ImGui.SliderFloat("Look (0-2)", ref _agxData.Look, 0.0f, 2.0f);
                break;
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

    private TonemapRoot BuildTonemapRoot(uint sourceIndex)
    {
        return _tonemapOperator switch
        {
            TonemapOperator.Reinhard => new TonemapRoot
            {
                Source = sourceIndex,
                Operator = 0,
                P0 = _reinhardData.MaxLuminance,
                P1 = _reinhardData.Gamma,
            },
            TonemapOperator.Uncharted2 => new TonemapRoot
            {
                Source = sourceIndex,
                Operator = 1,
                P0 = _uncharted2Data.A,
                P1 = _uncharted2Data.B,
                P2 = _uncharted2Data.C,
                P3 = _uncharted2Data.D,
                P4 = _uncharted2Data.E,
                P5 = _uncharted2Data.F,
                P6 = _uncharted2Data.W,
                P7 = _uncharted2Data.Exposure,
                P8 = _uncharted2Data.Gamma,
            },
            TonemapOperator.Filmic => new TonemapRoot
            {
                Source = sourceIndex,
                Operator = 2,
                P0 = _filmicData.Exposure,
                P1 = _filmicData.Gamma,
            },
            TonemapOperator.ACES => new TonemapRoot
            {
                Source = sourceIndex,
                Operator = 3,
                P0 = _acesData.A,
                P1 = _acesData.B,
                P2 = _acesData.C,
                P3 = _acesData.D,
                P4 = _acesData.E,
                P5 = _acesData.Exposure,
                P6 = _acesData.Gamma,
            },
            TonemapOperator.Neutral => new TonemapRoot
            {
                Source = sourceIndex,
                Operator = 4,
                P0 = _neutralData.Exposure,
                P1 = _neutralData.Gamma,
                P2 = _neutralData.StartCompression,
                P3 = _neutralData.Desaturation,
            },
            _ => new TonemapRoot
            {
                Source = sourceIndex,
                Operator = 5,
                P0 = _agxData.Exposure,
                P1 = _agxData.Gamma,
                P2 = _agxData.Look,
            },
        };
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

    private sealed class TonemapOutputNode(Game game) : RenderGraphNode("TonemapOutput")
    {
        private RGTexture _input;
        private RGTexture _output;

        protected override void SetupPass(RenderGraphBuilder builder, RenderGraphPassBuilder pass)
        {
            _input = game._chain.Current;
            _output = builder.Output;
            if (!_input.IsValid || !_output.IsValid)
            {
                return;
            }

            pass.Read(_input, RGUsage.PixelRead);
            pass.Write(_output, RGUsage.ColorAttachmentWrite);
            pass.ProducesOutput();
        }

        protected override void ExecutePass(in RenderGraphContext context)
        {
            if (!_input.IsValid || !_output.IsValid)
            {
                return;
            }

            Extent2D viewport = context.Viewport;
            Texture hdrTex = context.GetTexture(_input);
            GPUAllocation<TonemapRoot> alloc = context.RenderContext.FrameData.Allocate<TonemapRoot>();
            *alloc.Cpu = game.BuildTonemapRoot(hdrTex.SampledIndex);

            ColorAttachment colorAttachment = new()
            {
                RenderView = context.GetRenderView(_output),
                Load = LoadOp.Discard,
                Store = StoreOp.Store,
            };
            RenderingDesc passDesc = new()
            {
                Colors = MemoryMarshal.CreateReadOnlySpan(ref colorAttachment, 1),
            };

            using RenderContext.RenderScope render = context.RenderContext.BeginRenderPass(passDesc);
            render.SetViewport(new Viewport { Width = viewport.X, Height = viewport.Y });
            render.SetScissor(new Scissor { Width = viewport.X, Height = viewport.Y });
            render.Draw(game._tonemapMaterial, alloc.GPURange, DepthStencilState.None, 3u);
        }
    }
}
