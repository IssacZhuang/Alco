using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Alco.Engine;
using Alco.Graphics;
using Alco.Graphics.LowLevel;
using Alco.IO;
using Alco.Rendering;

namespace Alco.ImGUI.Test;

/// <summary>
/// End-to-end tests for <see cref="ImGUIRenderer"/> and <see cref="ImGUISystem"/> across NoGPU and GPU backends.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class TestImGuiRenderer
{
    private sealed class OverlayCaptureGame : GameEngine
    {
        private int _frames;
        private Task<RenderCaptureResult>? _captureTask;

        public RenderCaptureResult? CaptureResult { get; private set; }

        public OverlayCaptureGame(GameEngineSetting setting) : base(setting)
        {
            AddSystem(new ImGUISystem(this));

            if (AssetSystem.TryLoadRaw(BuiltInAssetsPath.Font_Default, out SafeMemoryHandle fontData))
            {
                ImGUIRenderer.Instance!.AddFontForLanguage(fontData.AsSpan(), FontLanguage.Basic);
            }
        }

        protected override void OnUpdate(float delta)
        {
            _frames++;
            if (_frames == 2 && _captureTask == null)
            {
                _captureTask = SwapchainCapture.RequestCaptureAsync();
            }

            if (_captureTask != null && _captureTask.IsCompleted)
            {
                CaptureResult = _captureTask.GetAwaiter().GetResult();
                Stop();
                return;
            }

            if (_frames > 240)
            {
                Stop();
                return;
            }

            if (MainPresenter.HasValidFrame)
            {
                ColorAttachment color = new()
                {
                    RenderView = MainPresenter.CurrentFrame.RenderView,
                    Load = LoadOp.Clear,
                    Store = StoreOp.Store,
                    Clear = new Vector4(0.0f, 0.0f, 0.0f, 1.0f),
                };
                RenderingDesc pass = new() { Colors = [color] };
                using (MainPresenter.FrameContext.BeginRenderPass(pass, [MainPresenter.SurfaceFormat]))
                {
                }
            }

            ImDrawListPtr foreground = ImGui.GetForegroundDrawList();
            foreground.AddRectFilled(
                new Vector2(20.0f, 20.0f),
                new Vector2(108.0f, 76.0f),
                ImGui.GetColorU32(new Vector4(0.0f, 1.0f, 0.0f, 1.0f)));
        }
    }

    /// <summary>
    /// Verifies NoGPU lifecycle, transient buffer pool growth, font atlas rebuild retirement, and zero leaked GPU resources.
    /// </summary>
    [Test]
    public void NoGpu_Lifecycle_BufferGrowth_And_FontRebuild_ReleaseAllResources()
    {
        GPUDevice device = Gpu.CreateDevice(new DeviceDesc { Backend = GraphicsBackend.NoGPU });
        RenderingSystem system = new(device, textureHeapByteCount: 8 * 1024 * 1024, bufferHeapByteCount: 8 * 1024 * 1024);
        using ShaderSystem shaderSystem = system.CreateShaderSystem();
        RenderContext context = new(system, frameDataByteCount: 256 * 1024);
        Material material = CreateImGuiMaterial(shaderSystem);

        Texture? rawSampled = null;
        Texture? offscreen = null;
        Texture2D? imageTex = null;
        ImGUIRenderer? renderer = null;
        try
        {
            renderer = new ImGUIRenderer(system, material, "NoGpuTest");
            rawSampled = system.CreateTexture(new TextureDesc
            {
                Extent = new Extent3D(16, 16, 1),
                Format = Format.Rgba8Unorm,
                Usage = TextureUsage.Sampled | TextureUsage.TransferDestination,
            });
            offscreen = system.CreateTexture(new TextureDesc
            {
                Extent = new Extent3D(128, 128, 1),
                Format = Format.Rgba8Unorm,
                Usage = TextureUsage.ColorAttachment,
            });
            imageTex = Texture2DUtility.CreateTexture2D(system, new byte[8 * 8 * 4], 8, 8);

            // Frame 1: exercise Image/ImageButton overloads (valid + null fallback) and overlay Draw.
            renderer.Begin(128, 128, 1.0f / 60.0f);
            ImGui.SetNextWindowPos(Vector2.Zero);
            ImGui.SetNextWindowSize(new Vector2(128, 128));
            if (ImGui.Begin("NoGpuPanel", ImGuiWindowFlags.NoSavedSettings))
            {
                ImGui.Text("Hello Alco.ImGUI");
                ImGui.Image(imageTex, new Vector2(16, 16));
                ImGui.Image(rawSampled, new Vector2(16, 16), new Rect(0, 0, 1, 1), new ColorFloat(1f, 1f, 1f, 1f));
                ImGui.Image((Texture2D?)null, new Vector2(8, 8));
                ImGui.Image((Texture?)null, new Vector2(8, 8));
                _ = ImGui.ImageButton("btn1", imageTex, new Vector2(16, 16));
                _ = ImGui.ImageButton("btn2", rawSampled, new Vector2(16, 16));
            }
            ImGui.End();
            renderer.Render();
            Assert.That(renderer.HasDrawData, Is.True);

            context.BeginFrame();
            renderer.Draw(context, offscreen.RenderView, new Extent2D(128, 128));
            context.Submit();

            // Rebuild font atlas while the previous frame is recorded/pending, then verify safe deferred retirement.
            string defaultFontPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts", "Default.ttf");
            Assert.That(File.Exists(defaultFontPath), Is.True, $"Missing deployed test font: {defaultFontPath}");
            byte[] fontBytes = File.ReadAllBytes(defaultFontPath);
            renderer.AddFontForLanguage(fontBytes, FontLanguage.Basic);

            // Frame 2: trigger font rebuild and draw >2000 quads (forces vertex/index transient buffer pool growth).
            renderer.Begin(128, 128, 0.0f);
            ImDrawListPtr fg = ImGui.GetForegroundDrawList();
            uint red = ImGui.GetColorU32(new Vector4(1f, 0f, 0f, 1f));
            for (int i = 0; i < 2000; i++)
            {
                float x = (i % 64) * 2.0f;
                float y = (i / 64) * 2.0f;
                fg.AddRectFilled(new Vector2(x, y), new Vector2(x + 2.0f, y + 2.0f), red);
            }
            renderer.Render();

            context.BeginFrame();
            using (RenderContext.RenderScope pass = context.BeginRenderPass(new RenderingDesc
            {
                Colors = [new ColorAttachment { RenderView = offscreen.RenderView, Load = LoadOp.Clear, Store = StoreOp.Store }],
            }))
            {
                renderer.Draw(in pass, context, new Extent2D(128, 128));
            }
            context.Submit().Wait();

            // Frame 3: reclaim retired font texture after Wait() and exercise secondary language merge.
            renderer.AddFontForLanguage(fontBytes, FontLanguage.Chinese);
            renderer.Begin(128, 128, 1.0f / 60.0f);
            ImGui.SetNextWindowPos(Vector2.Zero);
            ImGui.SetNextWindowSize(new Vector2(128, 128));
            if (ImGui.Begin("NoGpuPanel", ImGuiWindowFlags.NoSavedSettings))
            {
                ImGui.Text("中文测试");
            }
            ImGui.End();
            renderer.Render();
            Assert.That(renderer.HasDrawData, Is.True);
            context.BeginFrame();
            renderer.Draw(context, offscreen.RenderView, new Extent2D(128, 128));
            context.Submit().Wait();
        }
        finally
        {
            renderer?.Dispose();
            imageTex?.Dispose();
            if (offscreen != null && !offscreen.IsDestroyed)
            {
                offscreen.Destroy();
            }
            if (rawSampled != null && !rawSampled.IsDestroyed)
            {
                rawSampled.Destroy();
            }
            context.Destroy();
            shaderSystem.Dispose();
            system.Destroy();
            device.Destroy();
        }
    }

    /// <summary>
    /// Verifies offscreen ImGui rendering across GPU backends: vertex colors, bindless Texture2D sampling, scissor clipping, and alpha blending.
    /// </summary>
    [TestCase(GraphicsBackend.Dx12)]
    [TestCase(GraphicsBackend.VulkanCompact)]
    [TestCase(GraphicsBackend.Vulkan)]
    [Category("AlcoGpu")]
    [Platform("Win")]
    public unsafe void Gpu_OffscreenRender_DrawsRectsImagesClippingAndAlphaBlend(GraphicsBackend backend)
    {
        const uint targetSize = 64;
        GPUDevice? device;
        try
        {
            device = Gpu.CreateDevice(new DeviceDesc { Backend = backend });
        }
        catch (GPUException ex) when (ex.Error == Error.Unsupported)
        {
            Assert.Ignore($"Backend {backend} is unsupported on this host: {ex.Message}");
            return;
        }

        RenderingSystem? system = null;
        ShaderSystem? shaderSystem = null;
        RenderContext? context = null;
        Texture? target = null;
        Texture2D? greenTex = null;
        ImGUIRenderer? renderer = null;
        GPUHeap readback = default;
        try
        {
            system = new RenderingSystem(device, textureHeapByteCount: 8 * 1024 * 1024, bufferHeapByteCount: 8 * 1024 * 1024);
            shaderSystem = system.CreateShaderSystem();
            context = new RenderContext(system, frameDataByteCount: 256 * 1024);
            Material material = CreateImGuiMaterial(shaderSystem);
            renderer = new ImGUIRenderer(system, material, "GpuOffscreenTest");

            target = system.CreateTexture(new TextureDesc
            {
                Extent = new Extent3D(targetSize, targetSize, 1),
                Format = Format.Rgba8Unorm,
                Usage = TextureUsage.ColorAttachment | TextureUsage.TransferSource,
            });
            readback = device.CreateGPUHeap(targetSize * targetSize * 4, MemoryType.Readback);

            byte[] greenPixels = new byte[4 * 4 * 4];
            for (int i = 0; i < greenPixels.Length; i += 4)
            {
                greenPixels[i + 0] = 0;
                greenPixels[i + 1] = 255;
                greenPixels[i + 2] = 0;
                greenPixels[i + 3] = 255;
            }
            greenTex = Texture2DUtility.CreateTexture2D(system, greenPixels, 4, 4, ImageLoadOption.Default with
            {
                Format = Format.Rgba8Unorm,
                Name = "green_4x4",
            });

            renderer.Begin(targetSize, targetSize, 1.0f / 60.0f);
            ImDrawListPtr fg = ImGui.GetForegroundDrawList();

            // 1. Scissor clipping: draw red rect over [0..32, 0..32] clipped to [0..16, 0..32].
            fg.PushClipRect(new Vector2(0, 0), new Vector2(16, 32), false);
            fg.AddRectFilled(new Vector2(0, 0), new Vector2(32, 32), ImGui.GetColorU32(new Vector4(1f, 0f, 0f, 1f)));
            fg.PopClipRect();

            // 2. Bindless Texture2D sampling via ImGui.GetTextureId (no AddTexture registration).
            fg.AddImage(ImGui.GetTextureId(greenTex), new Vector2(32, 0), new Vector2(64, 32));

            // 3. Alpha blending: opaque green base at [0..32, 32..64] + 50% alpha blue overlay.
            fg.AddRectFilled(new Vector2(0, 32), new Vector2(32, 64), ImGui.GetColorU32(new Vector4(0f, 1f, 0f, 1f)));
            fg.AddRectFilled(new Vector2(0, 32), new Vector2(32, 64), ImGui.GetColorU32(new Vector4(0f, 0f, 1f, 0.5f)));

            // 4. ImGui.Image(Texture2D, ...) inside a borderless window covering [32..64, 32..64].
            ImGui.SetNextWindowPos(new Vector2(32, 32));
            ImGui.SetNextWindowSize(new Vector2(32, 32));
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0.0f);
            ImGuiWindowFlags flags = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoBackground |
                ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoInputs |
                ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;
            if (ImGui.Begin("ImageWindow", flags))
            {
                ImGui.Image(greenTex, new Vector2(32, 32));
            }
            ImGui.End();
            ImGui.PopStyleVar(2);

            renderer.Render();

            context.BeginFrame();
            using (context.BeginRenderPass(new RenderingDesc
            {
                Colors = [new ColorAttachment { RenderView = target.RenderView, Load = LoadOp.Clear, Store = StoreOp.Store, Clear = new Vector4(0f, 0f, 0f, 1f) }],
            }))
            {
            }
            renderer.Draw(context, target.RenderView, new Extent2D(targetSize, targetSize));
            context.Barrier(Stage.ColorOutput, Access.ColorWrite, Stage.Transfer, Access.TransferRead);
            context.CopyTextureToMemory(target.NativeTexture, readback.Range.GPURange);
            context.Barrier(Stage.Transfer, Access.TransferWrite, Stage.Host, Access.HostRead);
            context.Submit().Wait();

            ReadOnlySpan<byte> pixels = readback.Range.CpuSpan;
            AssertPixel(pixels, targetSize, 8, 16, 255, 0, 0, 255, "unclipped red rectangle");
            AssertPixel(pixels, targetSize, 24, 16, 0, 0, 0, 255, "scissor-clipped region remains black");
            AssertPixel(pixels, targetSize, 48, 16, 0, 255, 0, 255, "bindless green AddImage");
            AssertPixel(pixels, targetSize, 16, 48, 0, 128, 128, 255, "alpha-blended blue over green", tolerance: 3);
            AssertPixel(pixels, targetSize, 48, 48, 0, 255, 0, 255, "ImGui.Image(Texture2D) inside window");
        }
        finally
        {
            device.WaitIdle();
            context?.DiscardFrame();
            renderer?.Dispose();
            greenTex?.Dispose();
            if (target != null && !target.IsDestroyed)
            {
                target.Destroy();
            }
            readback.Destroy();
            context?.Destroy();
            shaderSystem?.Dispose();
            system?.Destroy();
            device.Destroy();
        }
    }

    /// <summary>
    /// Verifies that <see cref="ImGUISystem"/> composites on top of the main view before <see cref="SwapchainCaptureSystem"/>.
    /// </summary>
    [TestCase(GraphicsBackend.Dx12)]
    [TestCase(GraphicsBackend.VulkanCompact)]
    [TestCase(GraphicsBackend.Vulkan)]
    [Category("AlcoGpu")]
    [Platform("Win")]
    [Apartment(ApartmentState.STA)]
    public unsafe void Gpu_GameEngine_ImGUISystem_SwapchainCapture_CompositesOnTop(GraphicsBackend backend)
    {
        const uint width = 128;
        const uint height = 96;
        GameEngineSetting setting = new()
        {
            StopWhenError = true,
            View = new ViewSetting((int)width, (int)height, "ImGUISystem Capture Test"),
            Graphics = GraphicsSetting.Default with { Backend = backend },
        };

        RenderCaptureResult? result;
        try
        {
            using OverlayCaptureGame game = new(setting);
            game.Run();
            result = game.CaptureResult;
        }
        catch (GPUException ex) when (ex.Error == Error.Unsupported)
        {
            Assert.Ignore($"Backend {backend} is unsupported on this host: {ex.Message}");
            return;
        }

        Assert.That(result, Is.Not.Null, "Swapchain capture did not complete within 240 frames.");
        Assert.That(result!.Success, Is.True, result.Error);
        Assert.That(result.Width, Is.EqualTo((int)width));
        Assert.That(result.Height, Is.EqualTo((int)height));
        Assert.That(result.PngBytes, Is.Not.Null.And.Not.Empty);

        fixed (byte* png = result.PngBytes!)
        {
            byte* decoded = ImageDecodeUtility.DecodePng(new ReadOnlySpan<byte>(png, result.PngBytes!.Length), out int w, out int h);
            try
            {
                Assert.That(w, Is.EqualTo((int)width));
                Assert.That(h, Is.EqualTo((int)height));

                ReadOnlySpan<byte> pixels = new(decoded, w * h * 4);
                AssertPixel(pixels, (uint)w, 64, 48, 0, 255, 0, 255, "ImGui green overlay center pixel");
                AssertPixel(pixels, (uint)w, 4, 4, 0, 0, 0, 255, "cleared black background outside overlay");
            }
            finally
            {
                NativeMemory.Free(decoded);
            }
        }
    }

    private static Material CreateImGuiMaterial(ShaderSystem shaderSystem)
    {
        Shader shader = ImGUIResourceHelper.GetImGUIShader(shaderSystem);
        Material material = shader.CreateMaterial("ImGuiTestMaterial");
        material.Blend = new BlendState
        {
            Enabled = true,
            Color = new BlendComponentState
            {
                Source = BlendFactor.SourceAlpha,
                Destination = BlendFactor.OneMinusSourceAlpha,
            },
            Alpha = new BlendComponentState
            {
                Source = BlendFactor.One,
                Destination = BlendFactor.OneMinusSourceAlpha,
            },
        };
        material.Rasterization = new RasterizationState { Cull = CullMode.None };
        material.DepthStencilState = default;
        return material;
    }

    private static void AssertPixel(ReadOnlySpan<byte> rgba, uint stridePixels, uint x, uint y,
        byte r, byte g, byte b, byte a, string label, int tolerance = 2)
    {
        int offset = (int)((y * stridePixels + x) * 4);
        Assert.That(rgba[offset + 0], Is.InRange((byte)Math.Max(0, r - tolerance), (byte)Math.Min(255, r + tolerance)), $"{label} (R)");
        Assert.That(rgba[offset + 1], Is.InRange((byte)Math.Max(0, g - tolerance), (byte)Math.Min(255, g + tolerance)), $"{label} (G)");
        Assert.That(rgba[offset + 2], Is.InRange((byte)Math.Max(0, b - tolerance), (byte)Math.Min(255, b + tolerance)), $"{label} (B)");
        Assert.That(rgba[offset + 3], Is.EqualTo(a), $"{label} (A)");
    }
}
