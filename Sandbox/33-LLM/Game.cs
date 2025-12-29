using System;
using System.ComponentModel;
using System.Numerics;
using Alco;
using Alco.Engine;
using Alco.Graphics;
using Alco.ImGUI;
using Alco.IO;
using Alco.LLM;
using Alco.Rendering;
using Microsoft.SemanticKernel;

namespace _33_LLM;

/// <summary>
/// Sandbox 33: LLM System demonstration.
/// </summary>
public class Game : GameEngine
{
    private static ColorFloat Color = new ColorFloat(1f, 0.5f, 0.5f, 1f);
    private static ColorFloat ColorHit = new ColorFloat(2.5f, 1.25f, 1.25f, 1f);

    private LLMSystem _llmSystem;
    private Preference _preference = null!;
    private string _modelId = "gpt-4o";
    private string _apiKey = "";
    private string _orgId = "";
    private string _customUri = "";

    private string _chatInput = "";
    private List<(string Role, string Content)> _chatHistory = new List<(string Role, string Content)>();
    private bool _isWaitingForResponse = false;

    // Rendering fields
    private readonly CameraPerspectiveBuffer _camera;
    private readonly Shader _shader;
    private readonly RenderContext _renderer;
    private readonly GraphicsMaterial _material;
    private readonly GraphicsValueBuffer<Matrix4x4> _cameraBuffer;
    private readonly Dictionary<string, Cube> _entities = new();

    public Game(GameEngineSetting setting) : base(setting)
    {
        _llmSystem = new LLMSystem();
        _llmSystem.AddPlugin(this);
        AddSystem(_llmSystem);
        _preference = new Preference(new DirectoryFileSystem(Environment.CurrentDirectory), "llm_config.json");

        if (AssetSystem.TryLoadRaw(BuiltInAssetsPath.Font_Default, out SafeMemoryHandle data))
        {
            var span = data.AsSpan();
            ImGUIRenderer.Instance!.AddFontForLanguage(span, FontLanguage.Chinese);
            ImGUIRenderer.Instance!.AddFontForLanguage(span, FontLanguage.Japanese);
            ImGUIRenderer.Instance!.AddFontForLanguage(span, FontLanguage.Korean);
            ImGUIRenderer.Instance!.AddFontForLanguage(span, FontLanguage.Cyrillic);
        }

        // Initialize rendering components
        _shader = AssetSystem.Load<Shader>(BuiltInAssetsPath.Shader_Unlit);
        _camera = RenderingSystem.CreateCameraPerspective(1.03f, 16f / 9, 0.1f, 1000);
        _camera.Transform.Position.X = -10;
        _camera.UpdateMatrixToGPU();

        _renderer = RenderingSystem.CreateRenderContext();
        _material = RenderingSystem.CreateMaterial(_shader, "Unlit");

        _cameraBuffer = RenderingSystem.CreateGraphicsValueBuffer(_camera.Data.ViewProjectionMatrix, "camera_buffer");
        _material.SetBuffer("_camera", _cameraBuffer);

        // Add initial cube
        var initialCube = CreateCube(Color);
        initialCube.transform.Position = new Vector3(2, 0, 0);
        initialCube.transform.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 8);
        _entities.Add("cube 1", initialCube);

        MainView.OnResize += OnMainWindowResize;
    }

    protected override void OnStart()
    {
        _modelId = _preference.GetString("ModelId", _modelId);
        _apiKey = _preference.GetString("ApiKey", _apiKey);
        _orgId = _preference.GetString("OrgId", _orgId);
        _customUri = _preference.GetString("CustomUri", _customUri);
    }

    protected override void OnStop()
    {
        _preference.SetString("ModelId", _modelId);
        _preference.SetString("ApiKey", _apiKey);
        _preference.SetString("OrgId", _orgId);
        _preference.SetString("CustomUri", _customUri);
        _preference.Save();
    }

    protected override void OnUpdate(float delta)
    {
        if (Input.IsKeyDown(KeyCode.Escape))
        {
            Stop();
        }

        // Rendering logic
        _renderer.Begin(MainFrameBuffer);
        foreach (var cube in _entities.Values)
        {
            cube.OnDraw(_renderer);
        }
        _renderer.End();

        // Simple interaction logic (hover effect for all cubes)
        Vector2 localMousePosition = MainView.MousePosition;
        Ray3D cameraRay = _camera.Data.ScreenPointToRay(localMousePosition, MainView.Size);

        foreach (var cube in _entities.Values)
        {
            bool hit = CollisionUtility3D.RayBox(cameraRay, cube.Shape, out RaycastHit3D _);
            cube.Color = hit ? ColorHit : Color;
        }

        RenderConfigWindow();
        RenderChatWindow();
    }

    private void OnMainWindowResize(uint2 size)
    {
        _camera.AspectRatio = (float)size.X / size.Y;
        _camera.UpdateMatrixToGPU();
        _cameraBuffer.UpdateBuffer(_camera.Data.ViewProjectionMatrix);
    }

    private Cube CreateCube(ColorFloat color)
    {
        Cube ent = new Cube(RenderingSystem.MeshCube, _material);
        ent.Color = color;
        return ent;
    }

    private void RenderConfigWindow()
    {
        ImGui.Begin("LLM System Configuration");

        ImGui.InputText("Model ID", ref _modelId, 128);
        ImGui.InputText("API Key", ref _apiKey, 128, ImGuiInputTextFlags.Password);
        ImGui.InputText("Org ID (Optional)", ref _orgId, 128);
        ImGui.InputText("Custom URI (Optional)", ref _customUri, 256);

        ImGui.Separator();

        if (_llmSystem.IsConnected)
        {
            ImGui.TextColored(new Vector4(0, 1, 0, 1), "Status: Connected");
            if (ImGui.Button("Disconnect"))
            {
                _llmSystem.Disconnect();
                _chatHistory.Clear();
            }
        }
        else
        {
            ImGui.TextColored(new Vector4(1, 0, 0, 1), "Status: Disconnected");
            if (ImGui.Button("Connect"))
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(_customUri) && Uri.TryCreate(_customUri, UriKind.Absolute, out var uri))
                    {
                        _llmSystem.Connect(_modelId, _apiKey, uri);
                    }
                    else
                    {
                        _llmSystem.Connect(_modelId, _apiKey, string.IsNullOrWhiteSpace(_orgId) ? null : _orgId);
                    }
                }
                catch (Exception)
                {
                    // Error is already logged in LLMSystem.Connect
                }
            }
        }

        ImGui.End();
    }

    private void RenderChatWindow()
    {
        if (!_llmSystem.IsConnected)
        {
            return;
        }

        ImGui.Begin("LLM Chat");

        // Chat History
        float footerHeightToReserve = ImGui.GetStyle().ItemSpacing.Y + ImGui.GetFrameHeightWithSpacing();
        ImGui.BeginChild("ScrollingRegion", new Vector2(0, -footerHeightToReserve), ImGuiChildFlags.None, ImGuiWindowFlags.HorizontalScrollbar);

        foreach (var (role, content) in _chatHistory)
        {
            if (role == "User")
            {
                ImGui.TextColored(new Vector4(0.6f, 0.8f, 1.0f, 1.0f), "[User]:");
            }
            else
            {
                ImGui.TextColored(new Vector4(0.6f, 1.0f, 0.6f, 1.0f), "[LLM]:");
            }
            ImGui.TextWrapped(content);
            ImGui.Spacing();
        }

        if (_isWaitingForResponse)
        {
            ImGui.TextDisabled("LLM is generating...");
        }

        if (ImGui.GetScrollY() >= ImGui.GetScrollMaxY())
        {
            ImGui.SetScrollHereY(1.0f);
        }

        ImGui.EndChild();

        ImGui.Separator();

        // Chat Input
        bool reclaimFocus = false;
        ImGuiInputTextFlags inputFlags = ImGuiInputTextFlags.EnterReturnsTrue;
        if (_isWaitingForResponse) inputFlags |= ImGuiInputTextFlags.ReadOnly;

        if (ImGui.InputText("##Input", ref _chatInput, 1024, inputFlags))
        {
            SendMessage();
            reclaimFocus = true;
        }

        // Auto-focus on window apparition
        ImGui.SetItemDefaultFocus();
        if (reclaimFocus)
        {
            ImGui.SetKeyboardFocusHere(-1); // Auto focus previous widget
        }

        ImGui.SameLine();
        if (ImGui.Button("Send") && !_isWaitingForResponse)
        {
            SendMessage();
        }

        ImGui.End();
    }

    private async void SendMessage()
    {
        if (string.IsNullOrWhiteSpace(_chatInput) || _isWaitingForResponse)
        {
            return;
        }

        string userMessage = _chatInput;
        _chatInput = "";
        _chatHistory.Add(("User", userMessage));
        _isWaitingForResponse = true;

        // Prepare a placeholder for the LLM response
        int llmMessageIndex = _chatHistory.Count;
        _chatHistory.Add(("LLM", ""));

        try
        {
            await foreach (var chunk in _llmSystem.ChatStreamingAsync(userMessage))
            {
                var currentContent = _chatHistory[llmMessageIndex].Content + chunk;
                _chatHistory[llmMessageIndex] = ("LLM", currentContent);
            }
        }
        catch (Exception ex)
        {
            _chatHistory.Add(("System", $"Error: {ex.Message}"));
        }
        finally
        {
            _isWaitingForResponse = false;
        }
    }

    [KernelFunction]
    [Description("Get the list of cubes")]
    public string ListCube()
    {
        return string.Join(", ", _entities.Keys);
    }

    [KernelFunction]
    [Description("Set the color of a cube")]
    public string SetCubeColor(
        [Description("The name of the cube to set the color of")] string cubeName,
        [Description("The color to set the cube to")] string color
        )
    {
        if (!_entities.TryGetValue(cubeName, out var cube))
        {
            return $"Cube {cubeName} not found";
        }
        if (!ColorFloat.TryParse(color, out var colorFloat))
        {
            return $"Invalid color: {color}";
        }
        cube.Color = colorFloat;
        return $"Cube {cubeName} color set to {color}";
    }
}


