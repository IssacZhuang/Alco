using System;
using System.Numerics;
using Alco;
using Alco.Engine;
using Alco.ImGUI;

namespace Alco.ImGUI;

public class ImGUIInputHandler: AutoDisposable
{
    private readonly Input _inputSystem;
    private readonly View _view;

    /// <summary>
    /// The constructor of the ImGUIInputHandler.
    /// </summary>
    /// <param name="inputSystem">The input system.</param>
    /// <param name="getMousePosition">The function to get the mouse position. 
    /// The <see cref="Input.MousePosition"/> is used if the function is not provided.
    /// The default mouse position getter might not be correct because it is the position relative to the screen, not the window.
    /// It it better to provide a custom mouse position getter that returns the position relative to the window.
    /// </param>
    public ImGUIInputHandler(Input inputSystem, View view)
    {
        _inputSystem = inputSystem;
        _view = view;

        _inputSystem.OnKeyDown += OnKeyDown;
        _inputSystem.OnKeyUp += OnKeyUp;
        _inputSystem.OnMouseDown += OnMouseDown;
        _inputSystem.OnMouseUp += OnMouseUp;
        _view.OnTextInput += OnTextInput;
    }

    public void Update()
    {
        ImGuiIOPtr io = ImGui.GetIO();
        // do not use _inputSystem.MousePosition, it is the position relative to the screen, not the window
        //io.AddMousePosEvent(_inputSystem.MousePosition.X, _inputSystem.MousePosition.Y);

        Vector2 mousePosition = _view.MousePosition;
        io.AddMousePosEvent(mousePosition.X, mousePosition.Y);

        io.AddMouseWheelEvent(0, _inputSystem.MouseWheelDelta);
    
    }

    protected override void Dispose(bool disposing)
    {
        _inputSystem.OnKeyDown -= OnKeyDown;
        _inputSystem.OnKeyUp -= OnKeyUp;
        _inputSystem.OnMouseDown -= OnMouseDown;
        _inputSystem.OnMouseUp -= OnMouseUp;
        _view.OnTextInput -= OnTextInput;
    }

    private unsafe void OnTextInput(string str)
    {
        ImGuiIOPtr io = ImGui.GetIO();
        fixed (char* ptr = str)
        {
            for (int i = 0; i < str.Length; i++)
            {
                io.AddInputCharacterUTF16(ptr[i]);
            }
        }
    }

    private void OnKeyDown(KeyCode key)
    {
        ImGuiIOPtr io = ImGui.GetIO();
        if (TryMapKey(key, out ImGuiKey result))
        {
            io.AddKeyEvent(result, true);
        }
    }

    private void OnKeyUp(KeyCode key)
    {
        ImGuiIOPtr io = ImGui.GetIO();
        if (TryMapKey(key, out ImGuiKey result))
        {
            io.AddKeyEvent(result, false);
        }
    }

    private void OnMouseDown(Mouse mouse)
    {
        ImGuiIOPtr io = ImGui.GetIO();
        if (TryMapMouse(mouse, out int result))
        {
            io.AddMouseButtonEvent(result, true);
        }
    }

    private void OnMouseUp(Mouse mouse)
    {
        ImGuiIOPtr io = ImGui.GetIO();
        if (TryMapMouse(mouse, out int result))
        {
            io.AddMouseButtonEvent(result, false);
        }
    }

    private static bool TryMapMouse(Mouse mouse, out int result)
    {
        switch (mouse)
        {
            case Mouse.Left:
                result = 0;
                return true;
            case Mouse.Right:
                result = 1;
                return true;
            case Mouse.Middle:
                result = 2;
                return true;
            case Mouse.Button4:
                result = 3;
                return true;
            case Mouse.Button5:
                result = 4;
                return true;
        }
        result = 0;
        return false;
    }


    private static bool TryMapKey(KeyCode key, out ImGuiKey result)
    {
        ImGuiKey keyToImGuiKeyShortcut(KeyCode keyToConvert, KeyCode startKey1, ImGuiKey startKey2)
        {
            int changeFromStart1 = (int)keyToConvert - (int)startKey1;
            return startKey2 + changeFromStart1;
        }

        if (key >= KeyCode.F1 && key <= KeyCode.F12)
        {
            result = keyToImGuiKeyShortcut(key, KeyCode.F1, ImGuiKey.F1);
            return true;
        }
        else if (key >= KeyCode.Keypad0 && key <= KeyCode.Keypad9)
        {
            result = keyToImGuiKeyShortcut(key, KeyCode.Keypad0, ImGuiKey.Keypad0);
            return true;
        }
        else if (key >= KeyCode.A && key <= KeyCode.Z)
        {
            result = keyToImGuiKeyShortcut(key, KeyCode.A, ImGuiKey.A);
            return true;
        }
        else if (key >= KeyCode.Number0 && key <= KeyCode.Number9)
        {
            result = keyToImGuiKeyShortcut(key, KeyCode.Number0, ImGuiKey._0);
            return true;
        }

        switch (key)
        {
            case KeyCode.ShiftLeft:
            case KeyCode.ShiftRight:
                result = ImGuiKey.ModShift;
                return true;
            case KeyCode.ControlLeft:
            case KeyCode.ControlRight:
                result = ImGuiKey.ModCtrl;
                return true;
            case KeyCode.AltLeft:
            case KeyCode.AltRight:
                result = ImGuiKey.ModAlt;
                return true;
            case KeyCode.SuperLeft:
            case KeyCode.SuperRight:
                result = ImGuiKey.ModSuper;
                return true;
            case KeyCode.Menu:
                result = ImGuiKey.Menu;
                return true;
            case KeyCode.Up:
                result = ImGuiKey.UpArrow;
                return true;
            case KeyCode.Down:
                result = ImGuiKey.DownArrow;
                return true;
            case KeyCode.Left:
                result = ImGuiKey.LeftArrow;
                return true;
            case KeyCode.Right:
                result = ImGuiKey.RightArrow;
                return true;
            case KeyCode.Enter:
                result = ImGuiKey.Enter;
                return true;
            case KeyCode.Escape:
                result = ImGuiKey.Escape;
                return true;
            case KeyCode.Space:
                result = ImGuiKey.Space;
                return true;
            case KeyCode.Tab:
                result = ImGuiKey.Tab;
                return true;
            case KeyCode.Backspace:
                result = ImGuiKey.Backspace;
                return true;
            case KeyCode.Insert:
                result = ImGuiKey.Insert;
                return true;
            case KeyCode.Delete:
                result = ImGuiKey.Delete;
                return true;
            case KeyCode.PageUp:
                result = ImGuiKey.PageUp;
                return true;
            case KeyCode.PageDown:
                result = ImGuiKey.PageDown;
                return true;
            case KeyCode.Home:
                result = ImGuiKey.Home;
                return true;
            case KeyCode.End:
                result = ImGuiKey.End;
                return true;
            case KeyCode.CapsLock:
                result = ImGuiKey.CapsLock;
                return true;
            case KeyCode.ScrollLock:
                result = ImGuiKey.ScrollLock;
                return true;
            case KeyCode.PrintScreen:
                result = ImGuiKey.PrintScreen;
                return true;
            case KeyCode.Pause:
                result = ImGuiKey.Pause;
                return true;
            case KeyCode.NumLock:
                result = ImGuiKey.NumLock;
                return true;
            case KeyCode.KeypadDivide:
                result = ImGuiKey.KeypadDivide;
                return true;
            case KeyCode.KeypadMultiply:
                result = ImGuiKey.KeypadMultiply;
                return true;
            case KeyCode.KeypadSubtract:
                result = ImGuiKey.KeypadSubtract;
                return true;
            case KeyCode.KeypadAdd:
                result = ImGuiKey.KeypadAdd;
                return true;
            case KeyCode.KeypadDecimal:
                result = ImGuiKey.KeypadDecimal;
                return true;
            case KeyCode.KeypadEnter:
                result = ImGuiKey.KeypadEnter;
                return true;
            case KeyCode.GraveAccent:
                result = ImGuiKey.GraveAccent;
                return true;
            case KeyCode.Minus:
                result = ImGuiKey.Minus;
                return true;
            case KeyCode.Equal:
                result = ImGuiKey.Equal;
                return true;
            case KeyCode.LeftBracket:
                result = ImGuiKey.LeftBracket;
                return true;
            case KeyCode.RightBracket:
                result = ImGuiKey.RightBracket;
                return true;
            case KeyCode.Semicolon:
                result = ImGuiKey.Semicolon;
                return true;
            case KeyCode.Apostrophe:
                result = ImGuiKey.Apostrophe;
                return true;
            case KeyCode.Comma:
                result = ImGuiKey.Comma;
                return true;
            case KeyCode.Period:
                result = ImGuiKey.Period;
                return true;
            case KeyCode.Slash:
                result = ImGuiKey.Slash;
                return true;
            case KeyCode.BackSlash:
                result = ImGuiKey.Backslash;
                return true;
            default:
                result = ImGuiKey.GamepadBack;
                return false;
        }
    }
}
