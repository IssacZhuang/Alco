using System;
using System.Numerics;
using Alco;
using Alco.Engine;
using Alco.Graphics;
using Alco.ImGUI;

public class Game : GameEngine
{
    public Game(GameEngineSetting setting) : base(setting)
    {
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
        bool open = true;
        ImGui.Begin("Gamepad Status", ref open);

        var pads = Input.GetGamepads();
        ImGui.Text($"Connected Gamepads: {pads.Count}");

        for (int i = 0; i < pads.Count; i++)
        {
            var p = pads[i];
            ImGui.Separator();
            ImGui.Text($"#{i}: {p.Name}");
            ImGui.Text($"Connected: {p.IsConnected}");

            // Axes
            Vector2 left = p.GetLeftStick();
            Vector2 right = p.GetRightStick();
            float lt = p.GetAxis(GamepadAxis.LeftTrigger);
            float rt = p.GetAxis(GamepadAxis.RightTrigger);

            ImGui.Text($"Left Stick: ({left.X:F3}, {left.Y:F3})");
            ImGui.Text($"Right Stick: ({right.X:F3}, {right.Y:F3})");
            ImGui.Text($"Left Trigger: {lt:F3}");
            ImGui.Text($"Right Trigger: {rt:F3}");

            // Buttons grid
            ImGui.Separator();
            DrawButton("South", p.IsButtonPressed(GamepadButton.South)); ImGui.SameLine();
            DrawButton("East", p.IsButtonPressed(GamepadButton.East)); ImGui.SameLine();
            DrawButton("West", p.IsButtonPressed(GamepadButton.West)); ImGui.SameLine();
            DrawButton("North", p.IsButtonPressed(GamepadButton.North));

            DrawButton("LB", p.IsButtonPressed(GamepadButton.LeftShoulder)); ImGui.SameLine();
            DrawButton("RB", p.IsButtonPressed(GamepadButton.RightShoulder)); ImGui.SameLine();
            DrawButton("L3", p.IsButtonPressed(GamepadButton.LeftStick)); ImGui.SameLine();
            DrawButton("R3", p.IsButtonPressed(GamepadButton.RightStick));

            DrawButton("Back", p.IsButtonPressed(GamepadButton.Back)); ImGui.SameLine();
            DrawButton("Start", p.IsButtonPressed(GamepadButton.Start)); ImGui.SameLine();
            DrawButton("Guide", p.IsButtonPressed(GamepadButton.Guide)); ImGui.SameLine();
            DrawButton("Touchpad", p.IsButtonPressed(GamepadButton.Touchpad));

            DrawButton("DPad Up", p.IsButtonPressed(GamepadButton.DPadUp)); ImGui.SameLine();
            DrawButton("DPad Down", p.IsButtonPressed(GamepadButton.DPadDown)); ImGui.SameLine();
            DrawButton("DPad Left", p.IsButtonPressed(GamepadButton.DPadLeft)); ImGui.SameLine();
            DrawButton("DPad Right", p.IsButtonPressed(GamepadButton.DPadRight));

            if (ImGui.Button($"Vibrate #{i}"))
            {
                try { p.SetVibration(0.75f, 0.2f); } catch { }
            }
        }

        if (pads.Count == 0)
        {
            ImGui.Separator();
            ImGui.Text("No gamepads detected. Connect a controller.");
        }

        ImGui.End();
    }

    private static void DrawButton(string label, bool pressed)
    {
        if (pressed)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.2f, 0.7f, 0.2f, 1f));
        }
        ImGui.Button(label);
        if (pressed)
        {
            ImGui.PopStyleColor();
        }
    }
}

