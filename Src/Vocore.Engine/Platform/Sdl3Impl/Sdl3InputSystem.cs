using System.Collections.Frozen;
using System.Numerics;
using SDL3;
using static SDL3.SDL3;

namespace Vocore.Engine;

public unsafe class Sdl3InputSystem : InputSystem
{
    private const int MaxKeyCount = 512;
    private const int MaxMouseCount = 16;

    private struct State
    {
        public fixed bool iskeyDown[MaxKeyCount];
        public fixed bool iskeyUp[MaxKeyCount];
        public fixed bool iskeyPressing[MaxKeyCount];
        public fixed bool isMouseDown[MaxMouseCount];
        public fixed bool isMouseUp[MaxMouseCount];
        public fixed bool isMousePressing[MaxMouseCount];
    }
    private static readonly FrozenDictionary<SDL_Keycode, int> SdlKeyMap = BuildSdlKepMap();


    private State _state;
    private Vector2 _lastMousePosition;
    private Vector2 _mousePosition;
    private Vector2 _mouseDelta;
    

    public override Vector2 MousePosition
    {
        get
        {
            return _mousePosition;
        }
        set
        {
            _ = SDL_WarpMouseGlobal((int)value.X, (int)value.Y);
            _mousePosition = value;
        }
    }

    public override Vector2 MouseDelta
    {
        get
        {
            return _mouseDelta;
        }
    }

    public Sdl3InputSystem()
    {
        _lastMousePosition = MousePosition;
        _mousePosition = MousePosition;
        _mouseDelta = new Vector2(0, 0);
    }

    internal void Update()
    {
        Vector2 tmp = default;
        SDL_GetGlobalMouseState(&tmp.X, &tmp.Y);
        _mousePosition = tmp;
        _mouseDelta = _mousePosition - _lastMousePosition;
        _lastMousePosition = _mousePosition;
        Reset();
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


    internal void OnSdlKeyDown(SDL_Keycode key)
    {
        int k = SdlKeyMap[key];
        _state.iskeyDown[k] = true;
        _state.iskeyPressing[k] = true;
    }

    internal void OnSdlKeyUp(SDL_Keycode key)
    {
        int k = SdlKeyMap[key];
        _state.iskeyUp[k] = true;
        _state.iskeyPressing[k] = false;
    }

    internal void OnSdlMouseButtonDown(uint button)
    {
        Mouse b = ConvertMosueButton(button);
        _state.isMouseDown[(int)b] = true;
        _state.isMousePressing[(int)b] = true;
    }

    internal void OnSdlMouseButtonUp(uint button)
    {
        Mouse b = ConvertMosueButton(button);
        _state.isMouseUp[(int)b] = true;
        _state.isMousePressing[(int)b] = false;
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
    }
    
    private static Mouse ConvertMosueButton(uint button)
    {
        return button switch
        {
            SDL_BUTTON_LEFT => Mouse.Left,
            SDL_BUTTON_MIDDLE => Mouse.Middle,
            SDL_BUTTON_RIGHT => Mouse.Right,
            SDL_BUTTON_X1 => Mouse.Button4,
            SDL_BUTTON_X2 => Mouse.Button5,
            _ => Mouse.Unknown,
        };
    }


    private static FrozenDictionary<SDL_Keycode, int> BuildSdlKepMap()
    {
        Dictionary<SDL_Keycode, int> keyMap = new();
        keyMap[SDL_Keycode.Space] = (int)KeyCode.Space;
        keyMap[SDL_Keycode.Apostrophe] = (int)KeyCode.Apostrophe;
        keyMap[SDL_Keycode.Comma] = (int)KeyCode.Comma;
        keyMap[SDL_Keycode.Minus] = (int)KeyCode.Minus;
        keyMap[SDL_Keycode.Period] = (int)KeyCode.Period;
        keyMap[SDL_Keycode.Slash] = (int)KeyCode.Slash;
        keyMap[SDL_Keycode._0] = (int)KeyCode.Number0;
        keyMap[SDL_Keycode._1] = (int)KeyCode.Number1;
        keyMap[SDL_Keycode._2] = (int)KeyCode.Number2;
        keyMap[SDL_Keycode._3] = (int)KeyCode.Number3;
        keyMap[SDL_Keycode._4] = (int)KeyCode.Number4;
        keyMap[SDL_Keycode._5] = (int)KeyCode.Number5;
        keyMap[SDL_Keycode._6] = (int)KeyCode.Number6;
        keyMap[SDL_Keycode._7] = (int)KeyCode.Number7;
        keyMap[SDL_Keycode._8] = (int)KeyCode.Number8;
        keyMap[SDL_Keycode._9] = (int)KeyCode.Number9;
        keyMap[SDL_Keycode.Semicolon] = (int)KeyCode.Semicolon;
        keyMap[SDL_Keycode.Equals] = (int)KeyCode.Equal;
        keyMap[SDL_Keycode.A] = (int)KeyCode.A;
        keyMap[SDL_Keycode.B] = (int)KeyCode.B;
        keyMap[SDL_Keycode.C] = (int)KeyCode.C;
        keyMap[SDL_Keycode.D] = (int)KeyCode.D;
        keyMap[SDL_Keycode.E] = (int)KeyCode.E;
        keyMap[SDL_Keycode.F] = (int)KeyCode.F;
        keyMap[SDL_Keycode.G] = (int)KeyCode.G;
        keyMap[SDL_Keycode.H] = (int)KeyCode.H;
        keyMap[SDL_Keycode.I] = (int)KeyCode.I;
        keyMap[SDL_Keycode.J] = (int)KeyCode.J;
        keyMap[SDL_Keycode.K] = (int)KeyCode.K;
        keyMap[SDL_Keycode.L] = (int)KeyCode.L;
        keyMap[SDL_Keycode.M] = (int)KeyCode.M;
        keyMap[SDL_Keycode.N] = (int)KeyCode.N;
        keyMap[SDL_Keycode.O] = (int)KeyCode.O;
        keyMap[SDL_Keycode.P] = (int)KeyCode.P;
        keyMap[SDL_Keycode.Q] = (int)KeyCode.Q;
        keyMap[SDL_Keycode.R] = (int)KeyCode.R;
        keyMap[SDL_Keycode.S] = (int)KeyCode.S;
        keyMap[SDL_Keycode.T] = (int)KeyCode.T;
        keyMap[SDL_Keycode.U] = (int)KeyCode.U;
        keyMap[SDL_Keycode.V] = (int)KeyCode.V;
        keyMap[SDL_Keycode.W] = (int)KeyCode.W;
        keyMap[SDL_Keycode.X] = (int)KeyCode.X;
        keyMap[SDL_Keycode.Y] = (int)KeyCode.Y;
        keyMap[SDL_Keycode.Z] = (int)KeyCode.Z;
        keyMap[SDL_Keycode.LeftBracket] = (int)KeyCode.LeftBracket;
        keyMap[SDL_Keycode.Backslash] = (int)KeyCode.BackSlash;
        keyMap[SDL_Keycode.RightBracket] = (int)KeyCode.RightBracket;
        keyMap[SDL_Keycode.Grave] = (int)KeyCode.GraveAccent;
        //world 1,2 ??
        keyMap[SDL_Keycode.Escape] = (int)KeyCode.Escape;
        keyMap[SDL_Keycode.Return] = (int)KeyCode.Enter;
        keyMap[SDL_Keycode.Tab] = (int)KeyCode.Tab;
        keyMap[SDL_Keycode.Backspace] = (int)KeyCode.Backspace;
        keyMap[SDL_Keycode.Insert] = (int)KeyCode.Insert;
        keyMap[SDL_Keycode.Delete] = (int)KeyCode.Delete;
        keyMap[SDL_Keycode.Right] = (int)KeyCode.Right;
        keyMap[SDL_Keycode.Left] = (int)KeyCode.Left;
        keyMap[SDL_Keycode.Down] = (int)KeyCode.Down;
        keyMap[SDL_Keycode.Up] = (int)KeyCode.Up;
        keyMap[SDL_Keycode.PageUp] = (int)KeyCode.PageUp;
        keyMap[SDL_Keycode.PageDown] = (int)KeyCode.PageDown;
        keyMap[SDL_Keycode.Home] = (int)KeyCode.Home;
        keyMap[SDL_Keycode.End] = (int)KeyCode.End;
        keyMap[SDL_Keycode.Capslock] = (int)KeyCode.CapsLock;
        keyMap[SDL_Keycode.ScrollLock] = (int)KeyCode.ScrollLock;
        keyMap[SDL_Keycode.NumLockClear] = (int)KeyCode.NumLock;
        keyMap[SDL_Keycode.PrintScreen] = (int)KeyCode.PrintScreen;
        keyMap[SDL_Keycode.Pause] = (int)KeyCode.Pause;
        //f1-f24
        keyMap[SDL_Keycode.F1] = (int)KeyCode.F1;
        keyMap[SDL_Keycode.F2] = (int)KeyCode.F2;
        keyMap[SDL_Keycode.F3] = (int)KeyCode.F3;
        keyMap[SDL_Keycode.F4] = (int)KeyCode.F4;
        keyMap[SDL_Keycode.F5] = (int)KeyCode.F5;
        keyMap[SDL_Keycode.F6] = (int)KeyCode.F6;
        keyMap[SDL_Keycode.F7] = (int)KeyCode.F7;
        keyMap[SDL_Keycode.F8] = (int)KeyCode.F8;
        keyMap[SDL_Keycode.F9] = (int)KeyCode.F9;
        keyMap[SDL_Keycode.F10] = (int)KeyCode.F10;
        keyMap[SDL_Keycode.F11] = (int)KeyCode.F11;
        keyMap[SDL_Keycode.F12] = (int)KeyCode.F12;
        keyMap[SDL_Keycode.F13] = (int)KeyCode.F13;
        keyMap[SDL_Keycode.F14] = (int)KeyCode.F14;
        keyMap[SDL_Keycode.F15] = (int)KeyCode.F15;
        keyMap[SDL_Keycode.F16] = (int)KeyCode.F16;
        keyMap[SDL_Keycode.F17] = (int)KeyCode.F17;
        keyMap[SDL_Keycode.F18] = (int)KeyCode.F18;
        keyMap[SDL_Keycode.F19] = (int)KeyCode.F19;
        keyMap[SDL_Keycode.F20] = (int)KeyCode.F20;
        keyMap[SDL_Keycode.F21] = (int)KeyCode.F21;
        keyMap[SDL_Keycode.F22] = (int)KeyCode.F22;
        keyMap[SDL_Keycode.F23] = (int)KeyCode.F23;
        keyMap[SDL_Keycode.F24] = (int)KeyCode.F24;
        //keypad
        keyMap[SDL_Keycode.Kp0] = (int)KeyCode.Keypad0;
        keyMap[SDL_Keycode.Kp1] = (int)KeyCode.Keypad1;
        keyMap[SDL_Keycode.Kp2] = (int)KeyCode.Keypad2;
        keyMap[SDL_Keycode.Kp3] = (int)KeyCode.Keypad3;
        keyMap[SDL_Keycode.Kp4] = (int)KeyCode.Keypad4;
        keyMap[SDL_Keycode.Kp5] = (int)KeyCode.Keypad5;
        keyMap[SDL_Keycode.Kp6] = (int)KeyCode.Keypad6;
        keyMap[SDL_Keycode.Kp7] = (int)KeyCode.Keypad7;
        keyMap[SDL_Keycode.Kp8] = (int)KeyCode.Keypad8;
        keyMap[SDL_Keycode.Kp9] = (int)KeyCode.Keypad9;
        keyMap[SDL_Keycode.KpDecimal] = (int)KeyCode.KeypadDecimal;
        keyMap[SDL_Keycode.KpDivide] = (int)KeyCode.KeypadDivide;
        keyMap[SDL_Keycode.KpMultiply] = (int)KeyCode.KeypadMultiply;
        keyMap[SDL_Keycode.KpMinus] = (int)KeyCode.KeypadSubtract;
        keyMap[SDL_Keycode.KpPlus] = (int)KeyCode.KeypadAdd;
        keyMap[SDL_Keycode.KpEnter] = (int)KeyCode.KeypadEnter;
        keyMap[SDL_Keycode.KpEquals] = (int)KeyCode.KeypadEqual;
        //left right shift, control, alt, super
        keyMap[SDL_Keycode.LeftShirt] = (int)KeyCode.ShiftLeft;
        keyMap[SDL_Keycode.LeftControl] = (int)KeyCode.ControlLeft;
        keyMap[SDL_Keycode.LeftAlt] = (int)KeyCode.AltLeft;
        keyMap[SDL_Keycode.LeftGui] = (int)KeyCode.SuperLeft;//windows key/ mac command key
        keyMap[SDL_Keycode.RightShirt] = (int)KeyCode.ShiftRight;
        keyMap[SDL_Keycode.RightControl] = (int)KeyCode.ControlRight;
        keyMap[SDL_Keycode.RightAlt] = (int)KeyCode.AltRight;
        keyMap[SDL_Keycode.RightGui] = (int)KeyCode.SuperRight;//windows key/ mac command key
        //menu
        keyMap[SDL_Keycode.Menu] = (int)KeyCode.Menu;

        return keyMap.ToFrozenDictionary();
    }

    public override void CopyToClipboard(ReadOnlySpan<char> text)
    {
        SDL_SetClipboardText(text);
    }

    public override ReadOnlySpan<char> GetClipboardText()
    {
        return SDL_GetClipboardText();
    }
}

// public enum KeyCode
// {
//     Unknown,
//     Space,
//     Apostrophe,
//     Comma,
//     Minus,
//     Period,
//     Slash,
//     Number0,
//     D0,
//     Number1,
//     Number2,
//     Number3,
//     Number4,
//     Number5,
//     Number6,
//     Number7,
//     Number8,
//     Number9,
//     Semicolon,
//     Equal,
//     A,
//     B,
//     C,
//     D,
//     E,
//     F,
//     G,
//     H,
//     I,
//     J,
//     K,
//     L,
//     M,
//     N,
//     O,
//     P,
//     Q,
//     R,
//     S,
//     T,
//     U,
//     V,
//     W,
//     X,
//     Y,
//     Z,
//     LeftBracket,
//     BackSlash,
//     RightBracket,
//     GraveAccent,
//     World1,
//     World2,
//     Escape,
//     Enter,
//     Tab,
//     Backspace,
//     Insert,
//     Delete,
//     Right,
//     Left,
//     Down,
//     Up,
//     PageUp,
//     PageDown,
//     Home,
//     End,
//     CapsLock,
//     ScrollLock,
//     NumLock,
//     PrintScreen,
//     Pause,
//     F1,
//     F2,
//     F3,
//     F4,
//     F5,
//     F6,
//     F7,
//     F8,
//     F9,
//     F10,
//     F11,
//     F12,
//     F13,
//     F14,
//     F15,
//     F16,
//     F17,
//     F18,
//     F19,
//     F20,
//     F21,
//     F22,
//     F23,
//     F24,
//     F25,
//     Keypad0,
//     Keypad1,
//     Keypad2,
//     Keypad3,
//     Keypad4,
//     Keypad5,
//     Keypad6,
//     Keypad7,
//     Keypad8,
//     Keypad9,
//     KeypadDecimal,
//     KeypadDivide,
//     KeypadMultiply,
//     KeypadSubtract,
//     KeypadAdd,
//     KeypadEnter,
//     KeypadEqual,
//     ShiftLeft,
//     ControlLeft,
//     AltLeft,
//     SuperLeft,
//     ShiftRight,
//     ControlRight,
//     AltRight,
//     SuperRight,
//     Menu,
// }