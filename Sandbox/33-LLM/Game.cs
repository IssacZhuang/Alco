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
    private LLMSystem _llmSystem;
    private LLMAgent? _llmAgent;
    private LLMSession? _llmSession;
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
        var initialCube = CreateCube(ColorFloat.White);
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

        if (_llmAgent != null)
        {
            ImGui.TextColored(new Vector4(0, 1, 0, 1), "Status: Connected");
            if (ImGui.Button("Disconnect"))
            {
                _llmAgent = null;
                _llmSession = null;
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
                         _llmAgent = _llmSystem.CreateAgentFromRemote(uri, _apiKey, _modelId, this);
                    }
                    else
                    {
                        // Fallback to OpenAI default URI if no custom URI is provided but we have API key
                        // Or handle OrgId if supported in CreateAgentFromRemote (currently not in signature)
                        // For now assuming custom URI or default OpenAI logic inside builder if URI is null?
                        // But CreateAgentFromRemote requires URI.
                        // Let's assume for Sandbox we just use the custom URI path for now or throw if empty.
                        if (string.IsNullOrWhiteSpace(_apiKey))
                        {
                            throw new Exception("API Key is required");
                        }
                        // Default OpenAI endpoint if not custom?
                        // The current API requires URI. Let's provide a way to use default OpenAI.
                        // Wait, previous code supported orgId and optional URI.
                        // We need to check LLMSystem API again. It requires URI.
                        // Let's use https://api.openai.com/v1 if custom URI is empty.
                        _llmAgent = _llmSystem.CreateAgentFromRemote(new Uri("https://api.openai.com/v1"), _apiKey, _modelId, this);
                    }
                    _llmSession = _llmAgent.CreateSession();
                }
                catch (Exception ex)
                {
                    _chatHistory.Add(("System", $"Connection Failed: {ex.Message}"));
                }
            }
        }

        ImGui.End();
    }

    private void RenderChatWindow()
    {
        if (_llmAgent == null)
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
            await foreach (var chunk in _llmSession!.ChatStreamingAsync(userMessage))
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
        [Description("The color to set the cube to, the format should be like #RRGGBBAA")] string color
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


