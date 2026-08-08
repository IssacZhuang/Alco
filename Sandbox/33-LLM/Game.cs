using System;
using System.ComponentModel;
using System.Numerics;
using System.Text.Json;
using Alco;
using Alco.Engine;
using Alco.Graphics;
using Alco.ImGUI;
using Alco.LLM;
using Alco.Rendering;

namespace _33_LLM;

/// <summary>
/// Sandbox 33: LLM System demonstration.
/// </summary>
public class Game : GameEngine
{
    public class SandboxPreference
    {
        public string ModelId { get; set; } = "gpt-4o";
        public string ApiKey { get; set; } = "";
        public string OrgId { get; set; } = "";
        public string CustomUri { get; set; } = "";
    }

    private LLMSystem _llmSystem;
    private LLMAgent? _llmAgent;
    private LLMSession? _llmSession;
    private SandboxPreference _preference;

    private string _modelId = "gpt-4o";
    private string _apiKey = "";
    private string _orgId = "";
    private string _customUri = "";

    private string _chatInput = "";
    private List<(string Role, string Content)> _chatHistory = new List<(string Role, string Content)>();
    private readonly Dictionary<string, int> _toolMessageIndexByCallId = new();
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
        AddSystem(new ImGUISystem(this));

        _llmSystem = new LLMSystem(this);
        AddSystem(_llmSystem);
        _preference = LoadPreference<SandboxPreference>("33-LLM", "config");

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
        _modelId = _preference.ModelId;
        _apiKey = _preference.ApiKey;
        _orgId = _preference.OrgId;
        _customUri = _preference.CustomUri;
    }

    protected override void OnStop()
    {
        _preference.ModelId = _modelId;
        _preference.ApiKey = _apiKey;
        _preference.OrgId = _orgId;
        _preference.CustomUri = _customUri;
        SavePreference("33-LLM", "config", _preference);
    }

    protected override void OnUpdate(float delta)
    {
        if (Input.IsKeyDown(KeyCode.Escape))
        {
            Stop();
        }

        // Rendering logic
        if (MainPresenter.FrameBuffer is not { } frameBuffer) return;
        _renderer.Begin(frameBuffer, ColorFloat.Black);
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
                         _llmAgent = _llmSystem.CreateAgent(new LLMAgentOptions
                         {
                             Endpoint = uri,
                             ApiKey = _apiKey,
                             ModelId = _modelId,
                             ToolInstances = new[] { this },
                         });
                    }
                    else
                    {
                        // Fallback to OpenAI default URI if no custom URI is provided
                        if (string.IsNullOrWhiteSpace(_apiKey))
                        {
                            throw new Exception("API Key is required");
                        }
                        _llmAgent = _llmSystem.CreateAgent(new LLMAgentOptions
                        {
                            Endpoint = new Uri("https://api.openai.com/v1"),
                            ApiKey = _apiKey,
                            ModelId = _modelId,
                            ToolInstances = new[] { this },
                        });
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
        ImGui.BeginChild("ScrollingRegion", new Vector2(0, -footerHeightToReserve), ImGuiChildFlags.None, ImGuiWindowFlags.None);

        foreach (var (role, content) in _chatHistory)
        {
            switch (role)
            {
                case "User":
                    ImGui.TextColored(new Vector4(0.6f, 0.8f, 1.0f, 1.0f), "[User]:");
                    break;
                case "Tool":
                    ImGui.TextColored(new Vector4(1.0f, 0.85f, 0.45f, 1.0f), "[Tool]:");
                    break;
                case "System":
                    ImGui.TextColored(new Vector4(1.0f, 0.55f, 0.55f, 1.0f), "[System]:");
                    break;
                default:
                    ImGui.TextColored(new Vector4(0.6f, 1.0f, 0.6f, 1.0f), "[LLM]:");
                    break;
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
        _toolMessageIndexByCallId.Clear();
        _isWaitingForResponse = true;

        try
        {
            int? llmMessageIndex = null;
            await foreach (var sessionEvent in _llmSession!.ChatEventsAsync(userMessage))
            {
                switch (sessionEvent)
                {
                    case TextDeltaEvent textDelta:
                        AppendAssistantText(ref llmMessageIndex, textDelta.Text);
                        break;
                    case ToolCallStartedEvent toolStarted:
                        AddToolStarted(toolStarted);
                        llmMessageIndex = null;
                        break;
                    case ToolCallCompletedEvent toolCompleted:
                        CompleteTool(toolCompleted);
                        llmMessageIndex = null;
                        break;
                    case ToolCallFailedEvent toolFailed:
                        FailTool(toolFailed);
                        llmMessageIndex = null;
                        break;
                }
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

    private void AppendAssistantText(ref int? llmMessageIndex, string text)
    {
        if (llmMessageIndex == null)
        {
            llmMessageIndex = _chatHistory.Count;
            _chatHistory.Add(("LLM", ""));
        }

        var currentContent = _chatHistory[llmMessageIndex.Value].Content + text;
        _chatHistory[llmMessageIndex.Value] = ("LLM", currentContent);
    }

    private void AddToolStarted(ToolCallStartedEvent toolStarted)
    {
        var content = $"Tool: {toolStarted.ToolName}\nArgs: {FormatDisplayValue(toolStarted.Arguments)}";
        int index = _chatHistory.Count;
        _chatHistory.Add(("Tool", content));

        if (!string.IsNullOrEmpty(toolStarted.CallId))
        {
            _toolMessageIndexByCallId[toolStarted.CallId] = index;
        }
    }

    private void CompleteTool(ToolCallCompletedEvent toolCompleted)
    {
        string status = $"Status: completed in {toolCompleted.Duration.TotalMilliseconds:F0}ms\nResult: {FormatDisplayValue(toolCompleted.Result)}";
        UpdateToolLine(toolCompleted.CallId, toolCompleted.ToolName, status);
    }

    private void FailTool(ToolCallFailedEvent toolFailed)
    {
        string status = $"Status: failed in {toolFailed.Duration.TotalMilliseconds:F0}ms\nError: {toolFailed.ErrorType}: {toolFailed.Error}";
        UpdateToolLine(toolFailed.CallId, toolFailed.ToolName, status);
    }

    private void UpdateToolLine(string callId, string toolName, string status)
    {
        if (!string.IsNullOrEmpty(callId)
            && _toolMessageIndexByCallId.TryGetValue(callId, out int index)
            && index >= 0
            && index < _chatHistory.Count)
        {
            var current = _chatHistory[index];
            _chatHistory[index] = ("Tool", $"{current.Content}\n{status}");
            _toolMessageIndexByCallId.Remove(callId);
            return;
        }

        _chatHistory.Add(("Tool", $"Tool: {toolName}\n{status}"));
    }

    private static string FormatDisplayValue(object? value)
    {
        if (value == null)
        {
            return "null";
        }

        if (value is string text)
        {
            return text;
        }

        try
        {
            return JsonSerializer.Serialize(value);
        }
        catch
        {
            return value.ToString() ?? string.Empty;
        }
    }

    [AgentFunction]
    [Description("Get the list of cubes")]
    public string ListCube()
    {
        return string.Join(", ", _entities.Keys);
    }

    [AgentFunction]
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

    [AgentFunction(IsOnAgentThread = true)]
    [Description("Hello form")]
    public string HelloForm([Description("The name of the person")] string name)
    {
        Thread.Sleep(500);
        return $"Hello {name} from Form";
    }
}
