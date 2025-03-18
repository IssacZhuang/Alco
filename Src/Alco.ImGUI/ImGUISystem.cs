using Alco.Engine;
using Alco.Rendering;
using Alco.Graphics;

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

        // Use embedded shader resource instead of built-in asset
        string shaderCode = ResourceHelper.GetEmbeddedResourceString("ImGui.hlsl");
        _shader = renderingSystem.CreateShader(shaderCode, "ImGui_Embedded", [
            new(){
                Elements = new VertexElement[] {
                    new(0, 0, VertexFormat.Float32x2, "POSITION"),
                    new(1, 8, VertexFormat.Float32x2, "TEXCOORD0"),
                    new(2, 16, VertexFormat.Unorm8x4, "COLOR"),//the imgui vertex use uint as color
                },
                Stride = 20,
                StepMode = VertexStepMode.Vertex,
            }
        ]);

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
        _shader.Dispose();
    }
}
