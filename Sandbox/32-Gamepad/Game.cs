using System;
using System.Numerics;
using Alco;
using Alco.Engine;
using Alco.Graphics;
using Alco.ImGUI;

/// <summary>
/// Sandbox 32: Gamepad tester that displays state and logs connect/disconnect events.
/// </summary>
public class Game : GameEngine
{
    public Game(GameEngineSetting setting) : base(setting)
    {
    }

    /// <summary>
    /// Subscribe to gamepad connect/disconnect events.
    /// </summary>
    protected override void OnStart()
    {
        Input.OnGamepadConnected += OnGamepadConnected;
        Input.OnGamepadDisconnected += OnGamepadDisconnected;

        // Subscribe to already connected gamepads
        var pads = Input.GetGamepads();
        for (int i = 0; i < pads.Count; i++)
        {
            pads[i].OnButtonDown += OnButtonDown;
            pads[i].OnButtonUp += OnButtonUp;
        }
    }

    /// <summary>
    /// Unsubscribe from gamepad events.
    /// </summary>
    protected override void OnStop()
    {
        Input.OnGamepadConnected -= OnGamepadConnected;
        Input.OnGamepadDisconnected -= OnGamepadDisconnected;

        // Unsubscribe from currently connected gamepads
        var pads = Input.GetGamepads();
        for (int i = 0; i < pads.Count; i++)
        {
            pads[i].OnButtonDown -= OnButtonDown;
            pads[i].OnButtonUp -= OnButtonUp;
        }
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

        FixedString128 str = new FixedString128();
        str += "Frame rate: ";
        str += FrameRate;

        ImGui.Text(str);

        str.Clear();

        var pads = Input.GetGamepads();

        str += "Connected Gamepads: ";
        str += pads.Count;
        ImGui.Text(str);
        str.Clear();

        for (int i = 0; i < pads.Count; i++)
        {
            var p = pads[i];
            ImGui.Separator();
            
            ImGui.Text(p.Name);

            str += "Connected: ";
            str += p.IsConnected;
            ImGui.Text(str);
            str.Clear();

            // Axes
            Vector2 left = p.GetLeftStick();
            Vector2 right = p.GetRightStick();
            float lt = p.GetAxis(GamepadAxis.LeftTrigger);
            float rt = p.GetAxis(GamepadAxis.RightTrigger);

            lt = math.round(lt, 3);
            rt = math.round(rt, 3);

            str += "Left Stick: ";
            str += left.X;
            str += ", ";
            str += left.Y;
            ImGui.Text(str);
            str.Clear();

            str += "Left Trigger: ";
            str += lt;
            ImGui.Text(str);
            str.Clear();

            str += "Right Trigger: ";
            str += rt;
            ImGui.Text(str);
            str.Clear();

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

            if (ImGui.Button($"Vibrate"))
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

    private static void OnGamepadConnected(Gamepad gamepad)
    {
        Log.Info($"Gamepad connected: {gamepad.Name}");
        gamepad.OnButtonDown += OnButtonDown;
        gamepad.OnButtonUp += OnButtonUp;
    }

    private static void OnGamepadDisconnected(Gamepad gamepad)
    {
        Log.Info($"Gamepad disconnected: {gamepad.Name}");
        gamepad.OnButtonDown -= OnButtonDown;
        gamepad.OnButtonUp -= OnButtonUp;
    }

    private static void OnButtonDown(GamepadButton button)
    {
        Log.Info($"Gamepad button down: {button}");
    }

    private static void OnButtonUp(GamepadButton button)
    {
        Log.Info($"Gamepad button up: {button}");
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

