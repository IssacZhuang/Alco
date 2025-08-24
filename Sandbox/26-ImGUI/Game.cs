using System.Numerics;
using Alco.Engine;
using Alco.Rendering;
using Alco;
using Alco.Graphics;
using Alco.ImGUI;
using Alco.IO;


public class Game : GameEngine
{
    private bool showDemoWindow = true;
    private bool showCustomWindow = true;
    private float sliderValue = 0.5f;
    private Vector3 color = new Vector3(0.4f, 0.7f, 0.2f);
    private bool toggleValue = true;

    private ColorFloat colorFloat = new ColorFloat(0.4f, 0.7f, 0.2f, 1.0f);

    private Texture2D _texture;

    public Game(GameEngineSetting setting) : base(setting)
    {
        _texture = AssetSystem.Load<Texture2D>("Textures/Grid.png");
        if (AssetSystem.TryLoadRaw(BuiltInAssetsPath.Font_Default, out SafeMemoryHandle data))
        {
            var span = data.AsSpan();
            ImGUIRenderer.Instance!.AddFontForLanguage(span, FontLanguage.Chinese);
            ImGUIRenderer.Instance!.AddFontForLanguage(span, FontLanguage.Japanese);
            ImGUIRenderer.Instance!.AddFontForLanguage(span, FontLanguage.Korean);
            ImGUIRenderer.Instance!.AddFontForLanguage(span, FontLanguage.Cyrillic);
        }
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

            ImGui.Text(strFramerate);
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

            ImGui.Separator();
            ImGui.Text("Chinese (Simplified): 中文测试 你好，世界！");
            ImGui.Text("Chinese (Traditional): 中文測試 你好，世界！");
            ImGui.Text("Japanese: 日本語テスト こんにちは、世界！");
            ImGui.Text("Korean: 한국어 테스트 안녕하세요, 세계!");
            ImGui.Text("Russian: Русский тест Привет, мир!");
            ImGui.Text("French: Français test Bonjour le monde !");
            ImGui.Text("German: Deutsch Test Hallo, Welt!");

            if (toggleValue)
            {
                ImGui.TextColored(new Vector4(1.0f, 0.0f, 0.0f, 1.0f), "Toggle is ON");
            }

            ImGui.Image(_texture, new Vector2(100, 100));

            ImGui.End();
        }
    }
}