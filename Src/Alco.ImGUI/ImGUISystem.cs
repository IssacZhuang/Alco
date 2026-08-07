using Alco.Engine;
using Alco.Rendering;
using Alco.Graphics;

namespace Alco.ImGUI;

public class ImGUISystem: BaseEngineSystem
{
    private readonly GameEngine _engine;
    private readonly Shader _shader;
    private readonly Material _material;
    private readonly ImGUIRenderer _imGUIRenderer;
    private readonly ImGUIInputHandler _imGUIInputHandler;

    public ImGUISystem(GameEngine engine)
    {
        _engine = engine;
        RenderingSystem renderingSystem = engine.RenderingSystem;

        // Use embedded shader resource instead of built-in asset
        _shader = ImGUIResourceHelper.GetImGUIShader(renderingSystem);

        _material = renderingSystem.CreateMaterial(_shader, "ImGuiMaterial");
        _material.BlendState = BlendState.AlphaBlend;
        _imGUIRenderer = new ImGUIRenderer(renderingSystem, _material, "ImGUIRenderer");

        _imGUIInputHandler = new ImGUIInputHandler(engine.Input, engine.MainView);
    }

    public override void OnBeginFrame(float deltaTime)
    {
        uint2 size = _engine.MainView.Size;
        _imGUIRenderer.Begin(size.X, size.Y, deltaTime);
    }

    public override void OnUpdate(float delta)
    {
        _imGUIInputHandler.Update();
    }

    /// <summary>
    /// Finalizes the ImGui frame and draws it on top of the resolved frame, so the UI
    /// colors are not affected by the pipeline's post-processing.
    /// </summary>
    public override void OnEndFrame(float deltaTime)
    {
        _imGUIRenderer.Render();

        GPUFrameBuffer? frameBuffer = _engine.MainPresenter.FrameBuffer;
        if (frameBuffer != null)
        {
            _imGUIRenderer.Draw(frameBuffer);
        }
    }

    public override void OnStop()
    {
        _imGUIInputHandler.Dispose();
        _imGUIRenderer.Dispose();
        _material.Dispose();
        _shader.Dispose();
    }
}
