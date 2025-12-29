using System;
using System.Numerics;
using Alco.Engine;
using Alco.LLM;
using Alco.ImGUI;

namespace _33_LLM;

/// <summary>
/// Sandbox 33: LLM System demonstration.
/// </summary>
public class Game : GameEngine
{
    private LLMSystem _llmSystem = null!;
    private string _modelId = "gpt-4o";
    private string _apiKey = "";
    private string _orgId = "";
    private string _customUri = "";

    private string _chatInput = "";
    private List<(string Role, string Content)> _chatHistory = new List<(string Role, string Content)>();
    private bool _isWaitingForResponse = false;

    public Game(GameEngineSetting setting) : base(setting)
    {
    }

    protected override void OnStart()
    {
        _llmSystem = new LLMSystem();
        AddSystem(_llmSystem);
    }

    protected override void OnUpdate(float delta)
    {
        if (Input.IsKeyDown(KeyCode.Escape))
        {
            Stop();
        }

        RenderConfigWindow();
        RenderChatWindow();
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
            ImGui.TextDisabled("LLM is thinking...");
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

        try
        {
            string response = await _llmSystem.ChatAsync(userMessage);
            _chatHistory.Add(("LLM", response));
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
}
