using System.Numerics;
using Alco.Engine;
using Alco.Rendering;
using Alco;
using Alco.Graphics;
using Alco.ImGUI;


public class Game : GameEngine
{
    private bool showDemoWindow = true;
    private bool showCustomWindow = true;
    private float sliderValue = 0.5f;
    private Vector3 color = new Vector3(0.4f, 0.7f, 0.2f);
    private bool toggleValue = true;

    public Game(GameEngineSetting setting) : base(setting)
    {
        
    }

    protected override void OnUpdate(float delta)
    {
        if (Input.IsKeyDown(KeyCode.Escape))
        {
            Stop();
        }

        RenderImGUIContent();
    }

    private void RenderImGUIContent()
    {
        // Show ImGUI demo window
        if (showDemoWindow)
        {
            ImGui.ShowDemoWindow(ref showDemoWindow);
        }

        // Create a custom window
        if (showCustomWindow)
        {
            ImGui.Begin("Custom Window", ref showCustomWindow);

            FixedString8 strFramerate = new FixedString8();
            strFramerate.Append(FrameRate);

            ImGui.Text(strFramerate.AsReadOnlySpan());
            ImGui.Text("Welcome to ImGUI in Alco Engine!");
            ImGui.Spacing();

            ImGui.Checkbox("Show Demo Window", ref showDemoWindow);

            ImGui.Separator();

            ImGui.Text("Controls Example:");
            ImGui.SliderFloat("Slider", ref sliderValue, 0.0f, 1.0f);
            ImGui.ColorEdit3("Color Picker", ref color);
            ImGui.Checkbox("Toggle Option", ref toggleValue);

            ImGui.Separator();

            if (ImGui.Button("Click Me"))
            {
                // Button action
                sliderValue = 0.5f;
            }

            ImGui.SameLine();

            if (ImGui.Button("Reset Color"))
            {
                color = new Vector3(0.4f, 0.7f, 0.2f);
            }

            ImGui.Spacing();

            if (toggleValue)
            {
                ImGui.TextColored(new Vector4(1.0f, 0.0f, 0.0f, 1.0f), "Toggle is ON");
            }

            ImGui.End();
        }
    }
}