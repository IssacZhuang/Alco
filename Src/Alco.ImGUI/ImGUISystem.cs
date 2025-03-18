
using Alco.Engine;
using Alco.Rendering;

namespace Alco.ImGUI;

public class ImGUISystem: BaseEngineSystem
{
    private readonly Shader _shader;
    private readonly Material _material;
    private readonly ImGUIRenderer _imGUIRenderer;
    private readonly WindowRenderTarget _mainRenderTarget;
    public ImGUISystem(GameEngine engine)
    {
        RenderingSystem renderingSystem = engine.Rendering;
        _shader = engine.BuiltInAssets.Shader_ImGui;
        _material = renderingSystem.CreateGraphicsMaterial(_shader, "ImGuiMaterial");
        _imGUIRenderer = new ImGUIRenderer(renderingSystem, _material, "ImGUIRenderer");
        _mainRenderTarget = engine.MainRenderTarget;
    }

    public override void OnBeginFrame(float deltaTime)
    {
        _imGUIRenderer.Begin(_mainRenderTarget.FrameBuffer, deltaTime);
    }

    public override void OnEndFrame(float deltaTime)
    {
        _imGUIRenderer.End();
    }

    public override void Dispose()
    {
        _imGUIRenderer.Dispose();
        _material.Dispose();

    }

}
