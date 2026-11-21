using System.Numerics;
using Alco.Engine;
using Alco.Graphics;
using Alco.Graphics.LowLevel;

namespace Alco.ImGUI;

/// <summary>
/// ImGui engine system. Registered via <see cref="GameEngine.AddSystem"/>; the engine
/// drives <see cref="OnUpdate"/> (frame begin + input) and <see cref="OnEndFrame"/>
/// (render + draw) automatically.
/// </summary>
public sealed class ImGUISystem : BaseEngineSystem
{
    private const ulong OverlayFrameDataByteCount = 256 * 1024;

    private readonly GameEngine _engine;
    private readonly Material _material;
    private readonly RenderContext _renderContext;
    private readonly ImGUIRenderer _imGUIRenderer;
    private readonly ImGUIInputHandler _imGUIInputHandler;

    /// <summary>Gets the ImGui renderer owned by this system.</summary>
    public ImGUIRenderer Renderer => _imGUIRenderer;

    /// <summary>
    /// Runs last among all systems so ImGui draws on top of everything,
    /// including the debug-stats overlay (<see cref="DebugStatsSystem"/>).
    /// </summary>
    public override int Order => int.MaxValue;

    /// <summary>
    /// Creates the ImGui system and its renderer and input handler for the given engine.
    /// </summary>
    /// <param name="engine">The engine that supplies the rendering system, input, and main view.</param>
    public ImGUISystem(GameEngine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        _engine = engine;

        Shader shader = ImGUIResourceHelper.GetImGUIShader(engine.ShaderSystem);
        _material = shader.CreateMaterial("ImGuiMaterial");
        _material.Blend = new BlendState
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
        _material.Rasterization = new RasterizationState { Cull = CullMode.None };
        _material.DepthStencilState = default;

        if (engine.MainPresenter.SurfaceFormat != Format.Undefined)
        {
            _ = _material.WarmUpPipeline([engine.MainPresenter.SurfaceFormat]);
        }

        RenderContext? renderContext = null;
        ImGUIRenderer? renderer = null;
        ImGUIInputHandler? inputHandler = null;
        try
        {
            renderContext = new RenderContext(engine.RenderingSystem, frameDataByteCount: OverlayFrameDataByteCount);
            renderer = new ImGUIRenderer(engine.RenderingSystem, _material, "ImGUIRenderer");
            inputHandler = new ImGUIInputHandler(engine.Input, engine.MainView);

            _renderContext = renderContext;
            _imGUIRenderer = renderer;
            _imGUIInputHandler = inputHandler;
        }
        catch
        {
            inputHandler?.Dispose();
            renderer?.Dispose();
            renderContext?.Destroy();
            throw;
        }
    }

    /// <inheritdoc />
    public override void OnUpdate(float delta)
    {
        uint2 size = _engine.MainView.Size;
        Vector2 framebufferScale = Vector2.One;
        if (_engine.MainPresenter.HasValidFrame && size.X > 0 && size.Y > 0)
        {
            Extent2D extent = _engine.MainPresenter.CurrentFrame.Extent;
            framebufferScale = new Vector2(extent.X / (float)size.X, extent.Y / (float)size.Y);
        }

        _imGUIRenderer.Begin(size.X, size.Y, delta, framebufferScale);
        _imGUIInputHandler.Update();
    }

    /// <summary>
    /// Finalizes the ImGui frame and records overlay draw commands for presentation.
    /// </summary>
    public override void OnEndFrame(float deltaTime)
    {
        _imGUIRenderer.Render();

        if (!_engine.MainPresenter.HasValidFrame || !_imGUIRenderer.HasDrawData)
        {
            return;
        }

        SwapchainFrame frame = _engine.MainPresenter.CurrentFrame;
        if (frame.RenderView == null)
        {
            return;
        }

        bool begun = false;
        try
        {
            _renderContext.BeginFrame();
            begun = true;
            _imGUIRenderer.Draw(_renderContext, frame.RenderView, frame.Extent);
            _engine.MainPresenter.EnqueueFrameContext(_renderContext);
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
        _imGUIInputHandler.Dispose();
        _imGUIRenderer.Dispose();
        _renderContext.Destroy();
    }
}
