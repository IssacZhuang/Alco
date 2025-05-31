using System;
using System.Numerics;
using Alco.Engine;
using Avalonia.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace Alco.Editor;

public class EditorInput : Input, IDisposable
{
    private const int MaxKeyCount = 512;
    private const int MaxMouseCount = 16;

    private struct State
    {
        public bool[] iskeyDown;
        public bool[] iskeyUp;
        public bool[] iskeyPressing;
        public bool[] isMouseDown;
        public bool[] isMouseUp;
        public bool[] isMousePressing;

        public State()
        {
            iskeyDown = new bool[MaxKeyCount];
            iskeyUp = new bool[MaxKeyCount];
            iskeyPressing = new bool[MaxKeyCount];
            isMouseDown = new bool[MaxMouseCount];
            isMouseUp = new bool[MaxMouseCount];
            isMousePressing = new bool[MaxMouseCount];
        }
    }

    private State _state = new();
    private Vector2 _lastMousePosition;
    private Vector2 _mousePosition;
    private Vector2 _mouseDelta;
    private float _mouseWheelDelta;
    private TopLevel? _topLevel;
    private Window _window;

    public override Vector2 MousePosition
    {
        get => _mousePosition;
        set
        {
            if (_topLevel != null)
            {
                // Attempt to set mouse position in Avalonia
                Log.Warning("Setting mouse position is not working in Avalonia");
                _mousePosition = value;
            }
        }
    }

    public override Vector2 MouseDelta => _mouseDelta;

    public override float MouseWheelDelta => _mouseWheelDelta;

    public EditorInput(Window window)
    {
        _window = window;
        _topLevel = window.GetVisualRoot() as TopLevel;
        window.PointerMoved += OnAvaloniaMouseMove;
        window.KeyDown += OnAvaloniaKeyDown;
        window.KeyUp += OnAvaloniaKeyUp;
        window.PointerPressed += OnAvaloniaMouseDown;
        window.PointerReleased += OnAvaloniaMouseUp;
        window.PointerWheelChanged += OnAvaloniaMouseWheel;
        Reset();
    }

    public void Dispose()
    {
        _window.PointerMoved -= OnAvaloniaMouseMove;
        _window.KeyDown -= OnAvaloniaKeyDown;
        _window.KeyUp -= OnAvaloniaKeyUp;
        _window.PointerPressed -= OnAvaloniaMouseDown;
        _window.PointerReleased -= OnAvaloniaMouseUp;
        _window.PointerWheelChanged -= OnAvaloniaMouseWheel;
    }

    public void Update()
    {
        _mouseDelta = _mousePosition - _lastMousePosition;
        _lastMousePosition = _mousePosition;
        Reset();
    }

    public override void CopyToClipboard(ReadOnlySpan<char> text)
    {
        _topLevel?.Clipboard?.SetTextAsync(text.ToString());
    }

    public override ReadOnlySpan<char> GetClipboardText()
    {
        return _topLevel?.Clipboard?.GetTextAsync().GetAwaiter().GetResult() ?? string.Empty;
    }

    public override bool IsKeyDown(KeyCode key)
    {
        return _state.iskeyDown[(int)key];
    }

    public override bool IsKeyPressing(KeyCode key)
    {
        return _state.iskeyPressing[(int)key];
    }

    public override bool IsKeyUp(KeyCode key)
    {
        return _state.iskeyUp[(int)key];
    }

    public override bool IsMouseDown(Mouse button)
    {
        return _state.isMouseDown[(int)button];
    }

    public override bool IsMousePressing(Mouse button)
    {
        return _state.isMousePressing[(int)button];
    }

    public override bool IsMouseScrolling(out Vector2 delta)
    {
        delta = default;
        return false;
    }

    public override bool IsMouseUp(Mouse button)
    {
        return _state.isMouseUp[(int)button];
    }

    public override bool IsMouseWheelScrolling(out float delta)
    {
        delta = _mouseWheelDelta;
        return _mouseWheelDelta != 0;
    }

    private void OnAvaloniaKeyDown(object? sender, KeyEventArgs e)
    {
        int k = ConvertAvaloniaKey(e.Key);
        _state.iskeyDown[k] = true;
        _state.iskeyPressing[k] = true;
    }

    private void OnAvaloniaKeyUp(object? sender, KeyEventArgs e)
    {
        int k = ConvertAvaloniaKey(e.Key);
        _state.iskeyUp[k] = true;
        _state.iskeyPressing[k] = false;
    }

    private void OnAvaloniaMouseMove(object? sender, PointerEventArgs e)
    {
        // Try to get TopLevel if it's null (might happen if handler attached early)
        if (_topLevel == null)
        {
            _topLevel = _window.GetVisualRoot() as TopLevel;
            if (_topLevel == null)
            {
                // Log.Warning("Could not get TopLevel in OnAvaloniaMouseMove."); // Optional: Add logging if needed
                // Fallback or decide how to handle this case
                // For now, let's keep using window coordinates as a fallback
                var fallbackPosition = e.GetPosition(_window);
                _mousePosition = new Vector2((float)fallbackPosition.X, (float)fallbackPosition.Y);
                return;
            }
        }

        // Get position relative to the window
        var position = e.GetPosition(_window);
        // Convert window position to screen position
        var screenPosition = _topLevel.PointToScreen(position);
        // Store screen position as Vector2 (with explicit casts)
        _mousePosition = new Vector2((float)screenPosition.X, (float)screenPosition.Y);
    }

    private void OnAvaloniaMouseDown(object? sender, PointerPressedEventArgs e)
    {
        Mouse button = ConvertAvaloniaMouseButton(e.GetCurrentPoint(null).Properties.PointerUpdateKind);
        _state.isMouseDown[(int)button] = true;
        _state.isMousePressing[(int)button] = true;
    }

    private void OnAvaloniaMouseUp(object? sender, PointerReleasedEventArgs e)
    {
        Mouse button = ConvertAvaloniaMouseButton(e.GetCurrentPoint(null).Properties.PointerUpdateKind);
        _state.isMouseUp[(int)button] = true;
        _state.isMousePressing[(int)button] = false;
    }

    private void OnAvaloniaMouseWheel(object? sender, PointerWheelEventArgs e)
    {
        _mouseWheelDelta = (float)e.Delta.Y;
    }

    private void Reset()
    {
        for (int i = 0; i < MaxKeyCount; i++)
        {
            _state.iskeyDown[i] = false;
            _state.iskeyUp[i] = false;
        }
        for (int i = 0; i < MaxMouseCount; i++)
        {
            _state.isMouseDown[i] = false;
            _state.isMouseUp[i] = false;
        }
        _mouseWheelDelta = 0;
    }

    private static int ConvertAvaloniaKey(Key key)
    {
        // Map Avalonia Key to our KeyCode
        return key switch
        {
            Key.Space => (int)KeyCode.Space,
            Key.Oem3 => (int)KeyCode.GraveAccent,
            Key.D1 => (int)KeyCode.Number1,
            Key.D2 => (int)KeyCode.Number2,
            Key.D3 => (int)KeyCode.Number3,
            Key.D4 => (int)KeyCode.Number4,
            Key.D5 => (int)KeyCode.Number5,
            Key.D6 => (int)KeyCode.Number6,
            Key.D7 => (int)KeyCode.Number7,
            Key.D8 => (int)KeyCode.Number8,
            Key.D9 => (int)KeyCode.Number9,
            Key.D0 => (int)KeyCode.Number0,
            Key.OemMinus => (int)KeyCode.Minus,
            Key.OemPlus => (int)KeyCode.Equal,
            Key.Back => (int)KeyCode.Backspace,
            Key.Tab => (int)KeyCode.Tab,
            Key.Q => (int)KeyCode.Q,
            Key.W => (int)KeyCode.W,
            Key.E => (int)KeyCode.E,
            Key.R => (int)KeyCode.R,
            Key.T => (int)KeyCode.T,
            Key.Y => (int)KeyCode.Y,
            Key.U => (int)KeyCode.U,
            Key.I => (int)KeyCode.I,
            Key.O => (int)KeyCode.O,
            Key.P => (int)KeyCode.P,
            Key.OemOpenBrackets => (int)KeyCode.LeftBracket,
            Key.OemCloseBrackets => (int)KeyCode.RightBracket,
            Key.OemPipe => (int)KeyCode.BackSlash,
            Key.CapsLock => (int)KeyCode.CapsLock,
            Key.A => (int)KeyCode.A,
            Key.S => (int)KeyCode.S,
            Key.D => (int)KeyCode.D,
            Key.F => (int)KeyCode.F,
            Key.G => (int)KeyCode.G,
            Key.H => (int)KeyCode.H,
            Key.J => (int)KeyCode.J,
            Key.K => (int)KeyCode.K,
            Key.L => (int)KeyCode.L,
            Key.OemSemicolon => (int)KeyCode.Semicolon,
            Key.OemQuotes => (int)KeyCode.Apostrophe,
            Key.Enter => (int)KeyCode.Enter,
            Key.LeftShift => (int)KeyCode.ShiftLeft,
            Key.Z => (int)KeyCode.Z,
            Key.X => (int)KeyCode.X,
            Key.C => (int)KeyCode.C,
            Key.V => (int)KeyCode.V,
            Key.B => (int)KeyCode.B,
            Key.N => (int)KeyCode.N,
            Key.M => (int)KeyCode.M,
            Key.OemComma => (int)KeyCode.Comma,
            Key.OemPeriod => (int)KeyCode.Period,
            Key.OemQuestion => (int)KeyCode.Slash,
            Key.RightShift => (int)KeyCode.ShiftRight,
            Key.LeftCtrl => (int)KeyCode.ControlLeft,
            Key.LWin => (int)KeyCode.SuperLeft,
            Key.LeftAlt => (int)KeyCode.AltLeft,
            Key.RightAlt => (int)KeyCode.AltRight,
            Key.RWin => (int)KeyCode.SuperRight,
            Key.RightCtrl => (int)KeyCode.ControlRight,
            Key.Apps => (int)KeyCode.Menu,

            // Function keys
            Key.F1 => (int)KeyCode.F1,
            Key.F2 => (int)KeyCode.F2,
            Key.F3 => (int)KeyCode.F3,
            Key.F4 => (int)KeyCode.F4,
            Key.F5 => (int)KeyCode.F5,
            Key.F6 => (int)KeyCode.F6,
            Key.F7 => (int)KeyCode.F7,
            Key.F8 => (int)KeyCode.F8,
            Key.F9 => (int)KeyCode.F9,
            Key.F10 => (int)KeyCode.F10,
            Key.F11 => (int)KeyCode.F11,
            Key.F12 => (int)KeyCode.F12,
            Key.F13 => (int)KeyCode.F13,
            Key.F14 => (int)KeyCode.F14,
            Key.F15 => (int)KeyCode.F15,
            Key.F16 => (int)KeyCode.F16,
            Key.F17 => (int)KeyCode.F17,
            Key.F18 => (int)KeyCode.F18,
            Key.F19 => (int)KeyCode.F19,
            Key.F20 => (int)KeyCode.F20,
            Key.F21 => (int)KeyCode.F21,
            Key.F22 => (int)KeyCode.F22,
            Key.F23 => (int)KeyCode.F23,
            Key.F24 => (int)KeyCode.F24,

            // Navigation and editing keys
            Key.Escape => (int)KeyCode.Escape,
            Key.PrintScreen => (int)KeyCode.PrintScreen,
            Key.Scroll => (int)KeyCode.ScrollLock,
            Key.Pause => (int)KeyCode.Pause,
            Key.Insert => (int)KeyCode.Insert,
            Key.Home => (int)KeyCode.Home,
            Key.PageUp => (int)KeyCode.PageUp,
            Key.Delete => (int)KeyCode.Delete,
            Key.End => (int)KeyCode.End,
            Key.PageDown => (int)KeyCode.PageDown,
            Key.Up => (int)KeyCode.Up,
            Key.Left => (int)KeyCode.Left,
            Key.Down => (int)KeyCode.Down,
            Key.Right => (int)KeyCode.Right,

            // Numpad keys
            Key.NumLock => (int)KeyCode.NumLock,
            Key.NumPad0 => (int)KeyCode.Keypad0,
            Key.NumPad1 => (int)KeyCode.Keypad1,
            Key.NumPad2 => (int)KeyCode.Keypad2,
            Key.NumPad3 => (int)KeyCode.Keypad3,
            Key.NumPad4 => (int)KeyCode.Keypad4,
            Key.NumPad5 => (int)KeyCode.Keypad5,
            Key.NumPad6 => (int)KeyCode.Keypad6,
            Key.NumPad7 => (int)KeyCode.Keypad7,
            Key.NumPad8 => (int)KeyCode.Keypad8,
            Key.NumPad9 => (int)KeyCode.Keypad9,
            Key.Divide => (int)KeyCode.KeypadDivide,
            Key.Multiply => (int)KeyCode.KeypadMultiply,
            Key.Subtract => (int)KeyCode.KeypadSubtract,
            Key.Add => (int)KeyCode.KeypadAdd,
            Key.Decimal => (int)KeyCode.KeypadDecimal,
            //not supported
            // Key.NumPadEnter => (int)KeyCode.KeypadEnter,
            // Key.NumPadEqual => (int)KeyCode.KeypadEqual,

            _ => (int)KeyCode.Unknown
        };
    }

    private static Mouse ConvertAvaloniaMouseButton(PointerUpdateKind kind)
    {
        return kind switch
        {
            PointerUpdateKind.LeftButtonPressed or PointerUpdateKind.LeftButtonReleased => Mouse.Left,
            PointerUpdateKind.RightButtonPressed or PointerUpdateKind.RightButtonReleased => Mouse.Right,
            PointerUpdateKind.MiddleButtonPressed or PointerUpdateKind.MiddleButtonReleased => Mouse.Middle,
            // Handle other buttons as needed
            _ => Mouse.Unknown,
        };
    }
}

