using System.Numerics;
using System.Runtime.InteropServices;
using Alco.Engine;
using Alco.Graphics;
using Alco.ImGUI;
using Alco.IO;
using Alco.Rendering;

/// <summary>
/// Example sandbox demonstrating <see cref="RGNode_FXAA"/> integration with <see cref="RenderGraph"/>,
/// <see cref="RenderChain"/>, depth-assisted anti-aliasing, and live ImGui controls.
/// </summary>
public sealed class Game : GameEngine
{
    private readonly RenderGraph _renderGraph;
    private readonly RenderContext _renderContext;
    private readonly RenderChain _chain = new();
    private readonly Material _blitMaterial;
    private readonly AntiAliasingScene _scene;
    private readonly RGNode_FXAA _fxaaNode;

    private readonly string? _screenshotPath;
    private readonly int _screenshotFrames;
    private int _frameCount;
    private SwapchainCaptureSystem? _swapchainCapture;
    private Task<RenderCaptureResult>? _screenshotRequest;
    private bool _screenshotSaved;

    /// <summary>Initializes the RenderGraph, test scene, and <see cref="RGNode_FXAA"/> post-process node.</summary>
    /// <param name="setting">Engine and view configuration.</param>
    /// <param name="args">Command-line arguments supporting <c>--fxaa</c>, <c>--fxaa-mode</c>, <c>--patterns</c>, <c>--rotate</c>, <c>--screenshot</c>, and <c>--frames</c>.</param>
    public Game(GameEngineSetting setting, string[] args) : base(setting)
    {
        _screenshotPath = GetArgValue(args, "--screenshot=");
        _screenshotFrames = int.TryParse(GetArgValue(args, "--frames="), out int frames) ? frames : 30;
        if (_screenshotPath != null)
        {
            _swapchainCapture = SwapchainCapture;
        }

        _renderGraph = new RenderGraph(RenderingSystem, new Extent2D(MainView.Size.X, MainView.Size.Y), "FXAASandbox3D");
        _renderContext = new RenderContext(RenderingSystem, frameDataByteCount: 512 * 1024);

        _blitMaterial = BuiltInAssets.Shader_BlitOpaque.CreateMaterial("sandbox_fxaa_output_blit");
        _blitMaterial.Rasterization = RasterizationState.CullNone;
        _blitMaterial.Blend = BlendState.Opaque;

        _scene = new AntiAliasingScene(RenderingSystem, RenderingSystem.Shaders, _chain);
        _fxaaNode = new RGNode_FXAA(RenderingSystem, _chain, _ => _scene.DepthTarget);

        ApplyCommandLineOptions(args);

        _renderGraph.Use(_scene.SceneNode);
        _renderGraph.Use(_fxaaNode);
        _renderGraph.Use(new OutputBlitNode(this));
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
            return;
        }

        ImGuiIOPtr io = ImGui.GetIO();
        if (!io.WantCaptureKeyboard && Input.IsKeyDown(KeyCode.Space))
        {
            _fxaaNode.IsEnabled = !_fxaaNode.IsEnabled;
        }

        _scene.Update(delta, Input, MainView, io.WantCaptureMouse, io.WantCaptureKeyboard);
        DrawFxaaControls();

        _frameCount++;
        if (_screenshotPath != null && _screenshotRequest == null && !_screenshotSaved && _frameCount >= _screenshotFrames)
        {
            _screenshotRequest = _swapchainCapture!.RequestCaptureAsync();
        }

        if (MainPresenter.HasValidFrame)
        {
            SwapchainFrame frame = MainPresenter.CurrentFrame;
            _scene.SetViewportExtent(frame.Extent);
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
        _scene.Destroy();
        _renderGraph.Destroy();
        _renderContext.Destroy();
    }

    private void DrawFxaaControls()
    {
        ImGui.SetNextWindowPos(new Vector2(14f, 14f), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new Vector2(360f, 540f), ImGuiCond.FirstUseEver);
        ImGui.Begin("FXAA Anti-Aliasing");

        bool fxaaEnabled = _fxaaNode.IsEnabled;
        if (ImGui.Checkbox("Enable Anti-Aliasing (FXAA)", ref fxaaEnabled))
        {
            _fxaaNode.IsEnabled = fxaaEnabled;
        }

        ImGui.Text($"Status: {(fxaaEnabled ? "ON" : "OFF")}   (Space: Toggle)");
        ImGui.Separator();

        ImGui.BeginDisabled(!fxaaEnabled);

        FXAAMode mode = _fxaaNode.Mode;
        if (ImGui.Combo("Mode", ref mode))
        {
            _fxaaNode.Mode = mode;
        }

        FXAAQuality quality = _fxaaNode.Quality;
        if (ImGui.Combo("Quality", ref quality))
        {
            _fxaaNode.Quality = quality;
        }

        FXAAExecutionMode execMode = _fxaaNode.ExecutionMode;
        if (ImGui.Combo("Execution", ref execMode))
        {
            _fxaaNode.ExecutionMode = execMode;
        }

        FXAAColorSpace colorSpace = _fxaaNode.ColorSpace;
        if (ImGui.Combo("Color Space", ref colorSpace))
        {
            _fxaaNode.ColorSpace = colorSpace;
        }

        float threshold = _fxaaNode.Threshold;
        if (ImGui.SliderFloat("Edge Threshold", ref threshold, 0.063f, 0.333f))
        {
            _fxaaNode.Threshold = threshold;
        }

        float thresholdMin = _fxaaNode.ThresholdMin;
        if (ImGui.SliderFloat("Min Threshold", ref thresholdMin, 0.015f, 0.125f))
        {
            _fxaaNode.ThresholdMin = thresholdMin;
        }

        float subpix = _fxaaNode.Subpix;
        if (ImGui.SliderFloat("Subpixel Blend", ref subpix, 0.0f, 1.0f))
        {
            _fxaaNode.Subpix = subpix;
        }

        if (_fxaaNode.Mode != FXAAMode.Luma)
        {
            float depthRel = _fxaaNode.DepthThresholdRel;
            if (ImGui.SliderFloat("Depth Rel", ref depthRel, 0.0f, 0.25f))
            {
                _fxaaNode.DepthThresholdRel = depthRel;
            }

            float depthMin = _fxaaNode.DepthThresholdMin;
            if (ImGui.SliderFloat("Depth Min", ref depthMin, 0.0f, 0.01f, "%.4f"))
            {
                _fxaaNode.DepthThresholdMin = depthMin;
            }

            float colorFloor = _fxaaNode.ColorVisibilityFloor;
            if (ImGui.SliderFloat("Color Floor", ref colorFloor, 0.0f, 0.125f, "%.4f"))
            {
                _fxaaNode.ColorVisibilityFloor = colorFloor;
            }
        }

        if (ImGui.Button("Reset FXAA Defaults"))
        {
            _fxaaNode.Mode = FXAAMode.Luma;
            _fxaaNode.Quality = FXAAQuality.Medium;
            _fxaaNode.ExecutionMode = FXAAExecutionMode.Auto;
            _fxaaNode.ColorSpace = FXAAColorSpace.Gamma;
            _fxaaNode.Threshold = 0.125f;
            _fxaaNode.ThresholdMin = 0.0312f;
            _fxaaNode.Subpix = 0.75f;
            _fxaaNode.DepthThresholdMin = 1.0f / 4096.0f;
            _fxaaNode.DepthThresholdRel = 1.0f / 64.0f;
            _fxaaNode.ColorVisibilityFloor = 1.0f / 64.0f;
        }

        ImGui.EndDisabled();

        ImGui.Separator();
        _scene.DrawImGuiControls();

        ImGui.Separator();
        RenderGraphStatistics stats = _renderGraph.Statistics;
        ImGui.Text($"FPS: {FrameRate}  Backend: {RenderingSystem.Device.ActiveBackend}");
        ImGui.Text($"RG Passes: {stats.AlivePassCount}/{stats.TotalPassCount} (culled {stats.CulledPassCount})");
        ImGui.Text($"RG Textures: {stats.ActivePhysicalTextureCount} physical / {stats.LogicalTransientTextureCount} logical");

        ImGui.End();
    }

    private void ApplyCommandLineOptions(string[] args)
    {
        string? fxaaArg = GetArgValue(args, "--fxaa=");
        if (string.Equals(fxaaArg, "off", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(fxaaArg, "false", StringComparison.OrdinalIgnoreCase))
        {
            _fxaaNode.IsEnabled = false;
        }

        string? modeArg = GetArgValue(args, "--fxaa-mode=");
        if (modeArg != null && Enum.TryParse(modeArg, ignoreCase: true, out FXAAMode parsedMode) && Enum.IsDefined(parsedMode))
        {
            _fxaaNode.Mode = parsedMode;
        }

        if (string.Equals(GetArgValue(args, "--patterns="), "off", StringComparison.OrdinalIgnoreCase))
        {
            _scene.ShowSurfacePatterns = false;
        }

        if (string.Equals(GetArgValue(args, "--rotate="), "off", StringComparison.OrdinalIgnoreCase))
        {
            _scene.AutoRotate = false;
        }
    }

    private void ProcessScreenshotCapture()
    {
        if (_screenshotRequest == null)
        {
            return;
        }

        if (_screenshotRequest.IsCompletedSuccessfully)
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
        else if (_screenshotRequest.IsFaulted)
        {
            Console.WriteLine($"Screenshot failed: {_screenshotRequest.Exception?.GetBaseException().Message}");
            Stop();
        }
    }

    private static string? GetArgValue(string[] args, string prefix)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].StartsWith(prefix, StringComparison.Ordinal))
            {
                return args[i][prefix.Length..];
            }
        }
        return null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BlitOpaqueRoot
    {
        public uint TextureIndex;
    }

    private sealed class OutputBlitNode(Game game) : RenderGraphNode("OutputBlit")
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

            Extent2D vp = context.Viewport;
            Texture sourceTex = context.GetTexture(_input);
            GPURange root = context.RenderContext.AllocateRoot(new BlitOpaqueRoot
            {
                TextureIndex = sourceTex.SampledIndex,
            });

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
            render.SetViewport(new Viewport { Width = vp.X, Height = vp.Y });
            render.SetScissor(new Scissor { Width = vp.X, Height = vp.Y });
            render.Draw(game._blitMaterial, root, DepthStencilState.None, 3u);
        }
    }
}
