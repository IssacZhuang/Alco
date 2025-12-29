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

        RenderImGUI();
    }

    private void RenderImGUI()
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
}
