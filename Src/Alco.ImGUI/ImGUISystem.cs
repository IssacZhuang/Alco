using Alco.Engine;
using Alco.Rendering;
using Alco.Graphics;

namespace Alco.ImGUI;

public class ImGUISystem: BaseEngineSystem
{
    private readonly Shader _shader;
    private readonly Material _material;
    private readonly ImGUIRenderer _imGUIRenderer;
    private readonly ImGUIInputHandler _imGUIInputHandler;
    private readonly ViewRenderTarget _mainRenderTarget;

    public ImGUISystem(GameEngine engine, ViewRenderTarget mainRenderTarget)
    {
        RenderingSystem renderingSystem = engine.Rendering;

        // Use embedded shader resource instead of built-in asset
        _shader = ImGUIResourceHelper.GetImGUIShader(renderingSystem);

        _material = renderingSystem.CreateGraphicsMaterial(_shader, "ImGuiMaterial");
        _material.BlendState = BlendState.AlphaBlend;
        _imGUIRenderer = new ImGUIRenderer(renderingSystem, _material, "ImGUIRenderer");
        _mainRenderTarget = mainRenderTarget;

        _imGUIInputHandler = new ImGUIInputHandler(engine.MainView, engine.Input);
    }

    public override void OnBeginFrame(float deltaTime)
    {
        _imGUIRenderer.Begin(_mainRenderTarget.FrameBuffer, deltaTime);
    }

    public override void OnUpdate(float delta)
    {
        _imGUIInputHandler.Update();
    }

    public override void OnEndFrame(float deltaTime)
    {
        _imGUIRenderer.End();
    }

    public override void OnStop()
    {
        _imGUIRenderer.Dispose();
        _material.Dispose();
        _shader.Dispose();
    }
}
