using Alco.Engine;
using Alco.Graphics;
using Alco.ImGUI;
using Alco.Rendering;
using ImGuiNET;

namespace Alco.Editor.Views;

public class TestGpuSurfaceView : GPUSurfaceView
{
    private readonly Shader _shader;
    private readonly Material _material;
    private readonly ImGUIRenderer _imGUIRenderer;

    public TestGpuSurfaceView()
    {
        GameEngine engine = App.Main.Engine;
        RenderingSystem renderingSystem = engine.Rendering;

        // Use embedded shader resource instead of built-in asset
        _shader = ImGUIResourceHelper.GetImGUIShader(renderingSystem);

        _material = renderingSystem.CreateGraphicsMaterial(_shader, "ImGuiMaterial");
        _material.BlendState = BlendState.AlphaBlend;
        _imGUIRenderer = new ImGUIRenderer(renderingSystem, _material, "ImGUIRenderer");
    }

    protected override void OnRender(GPUFrameBuffer frameBuffer, float deltaTime)
    {
        base.OnRender(frameBuffer, deltaTime);
        _imGUIRenderer.Begin(frameBuffer, deltaTime);

        ImGui.Begin("Test");
        ImGui.Text("Hello, World!");
        ImGui.End();

        _imGUIRenderer.End();
    }

}

