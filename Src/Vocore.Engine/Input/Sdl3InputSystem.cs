using System.Numerics;
using SDL3;
using static SDL3.SDL3;

namespace Vocore.Engine;

public unsafe class Sdl3InputSystem : InputSystem
{
    private SDL_Window _window;

    private const int MaxKeyCount = 512;
    private const int MaxMouseCount = 16;

    internal event Action<uint2>? OnWindowResize;

    private struct State
    {
        public fixed bool iskeyDown[MaxKeyCount];
        public fixed bool iskeyUp[MaxKeyCount];
        public fixed bool iskeyPressing[MaxKeyCount];
        public fixed bool isMouseDown[MaxMouseCount];
        public fixed bool isMouseUp[MaxMouseCount];
        public fixed bool isMousePressing[MaxMouseCount];
    }

    public override Vector2 MousePosition
    {
        get
        {
            Vector2 result = default;
            SDL_GetMouseState(&result.X, &result.Y);
            return result;
        }
        set
        {
            SDL_WarpMouseInWindow(_window, (int)value.X, (int)value.Y);
        }
    }

    public override Vector2 MouseDelta
    {
        get
        {
            return new Vector2(0, 0);
        }
    }

    public override bool ForceMouseInScreenCenter
    {
        get
        {
            return false;
        }
        set
        {

        }
    }

    public Sdl3InputSystem(SDL_Window window)
    {
        _window = window;
    }

    public override bool IsKeyDown(KeyCode key)
    {
        return false;
    }

    public override bool IsKeyPressing(KeyCode key)
    {
        return false;
    }

    public override bool IsKeyUp(KeyCode key)
    {
        return false;
    }

    public override bool IsMouseDown(Mouse button)
    {
        return false;
    }

    public override bool IsMousePressing(Mouse button)
    {
        return false;
    }

    public override bool IsMouseScrolling(out Vector2 delta)
    {
        delta = default;
        return false;
    }

    public override bool IsMouseUp(Mouse button)
    {
        return false;
    }

    internal override void DoEvent()
    {
        while (SDL_PollEvent(out SDL_Event e))
        {
            switch (e.type)
            {
                case SDL_EventType.WindowResized:
                    OnWindowResize?.Invoke(new uint2(e.window.data1, e.window.data2));
                    break;
                case SDL_EventType.MouseButtonDown:
                    Log.Info("Mouse Down: " + e.button.button);
                    break;
                case SDL_EventType.MouseButtonUp:
                    Log.Info("Mouse Up: " + e.button.button);
                    break;
                case SDL_EventType.KeyDown:
                    SDL_Keycode key = e.key.key;
                    Log.Info("Key Down: " + key);
                    break;
                case SDL_EventType.KeyUp:
                    Log.Info("Key Up: " + e.key.key);
                    break;
            }
        }
    }

    internal override void Reset()
    {

    }

    internal override void Update()
    {

    }

    private static int[] BuildSdlKepMap()
    {
        int[] keyMap = new int[MaxKeyCount];
        keyMap[(int)SDL_Keycode.Space] = (int)KeyCode.Space;
        keyMap[(int)SDL_Keycode.Apostrophe] = (int)KeyCode.Apostrophe;
        keyMap[(int)SDL_Keycode.Comma] = (int)KeyCode.Comma;
        keyMap[(int)SDL_Keycode.Minus] = (int)KeyCode.Minus;
        keyMap[(int)SDL_Keycode.Period] = (int)KeyCode.Period;
        keyMap[(int)SDL_Keycode.Slash] = (int)KeyCode.Slash;
        keyMap[(int)SDL_Keycode._0] = (int)KeyCode.Number0;
        keyMap[(int)SDL_Keycode._1] = (int)KeyCode.Number1;
        keyMap[(int)SDL_Keycode._2] = (int)KeyCode.Number2;
        keyMap[(int)SDL_Keycode._3] = (int)KeyCode.Number3;
        keyMap[(int)SDL_Keycode._4] = (int)KeyCode.Number4;
        keyMap[(int)SDL_Keycode._5] = (int)KeyCode.Number5;
        keyMap[(int)SDL_Keycode._6] = (int)KeyCode.Number6;
        keyMap[(int)SDL_Keycode._7] = (int)KeyCode.Number7;
        keyMap[(int)SDL_Keycode._8] = (int)KeyCode.Number8;
        keyMap[(int)SDL_Keycode._9] = (int)KeyCode.Number9;
        keyMap[(int)SDL_Keycode.Semicolon] = (int)KeyCode.Semicolon;
        keyMap[(int)SDL_Keycode.Equals] = (int)KeyCode.Equal;
        keyMap[(int)SDL_Keycode.A] = (int)KeyCode.A;
        keyMap[(int)SDL_Keycode.B] = (int)KeyCode.B;
        keyMap[(int)SDL_Keycode.C] = (int)KeyCode.C;
        keyMap[(int)SDL_Keycode.D] = (int)KeyCode.D;
        keyMap[(int)SDL_Keycode.E] = (int)KeyCode.E;
        keyMap[(int)SDL_Keycode.F] = (int)KeyCode.F;
        keyMap[(int)SDL_Keycode.G] = (int)KeyCode.G;
        keyMap[(int)SDL_Keycode.H] = (int)KeyCode.H;
        keyMap[(int)SDL_Keycode.I] = (int)KeyCode.I;
        keyMap[(int)SDL_Keycode.J] = (int)KeyCode.J;
        keyMap[(int)SDL_Keycode.K] = (int)KeyCode.K;
        keyMap[(int)SDL_Keycode.L] = (int)KeyCode.L;
        keyMap[(int)SDL_Keycode.M] = (int)KeyCode.M;
        keyMap[(int)SDL_Keycode.N] = (int)KeyCode.N;
        keyMap[(int)SDL_Keycode.O] = (int)KeyCode.O;
        keyMap[(int)SDL_Keycode.P] = (int)KeyCode.P;
        keyMap[(int)SDL_Keycode.Q] = (int)KeyCode.Q;
        keyMap[(int)SDL_Keycode.R] = (int)KeyCode.R;
        keyMap[(int)SDL_Keycode.S] = (int)KeyCode.S;
        keyMap[(int)SDL_Keycode.T] = (int)KeyCode.T;
        keyMap[(int)SDL_Keycode.U] = (int)KeyCode.U;
        keyMap[(int)SDL_Keycode.V] = (int)KeyCode.V;
        keyMap[(int)SDL_Keycode.W] = (int)KeyCode.W;
        keyMap[(int)SDL_Keycode.X] = (int)KeyCode.X;
        keyMap[(int)SDL_Keycode.Y] = (int)KeyCode.Y;
        keyMap[(int)SDL_Keycode.Z] = (int)KeyCode.Z;
        keyMap[(int)SDL_Keycode.LeftBracket] = (int)KeyCode.LeftBracket;
        keyMap[(int)SDL_Keycode.Backslash] = (int)KeyCode.BackSlash;
        keyMap[(int)SDL_Keycode.RightBracket] = (int)KeyCode.RightBracket;
        keyMap[(int)SDL_Keycode.Grave] = (int)KeyCode.GraveAccent;
      
        return keyMap;
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
//     Paus,
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