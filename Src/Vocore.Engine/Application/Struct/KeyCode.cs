using System.Runtime.CompilerServices;
using Silk.NET.Input;

namespace Vocore.Engine;

public readonly struct KeyCode
{
    //copy from Silk.NET.Input.Key
    public static readonly KeyCode Unknown = Key.Unknown;
    public static readonly KeyCode Space = Key.Space;
    public static readonly KeyCode Apostrophe = Key.Apostrophe;
    public static readonly KeyCode Comma = Key.Comma;
    public static readonly KeyCode Minus = Key.Minus;
    public static readonly KeyCode Period = Key.Period;
    public static readonly KeyCode Slash = Key.Slash;
    public static readonly KeyCode Number0 = Key.Number0;
    public static readonly KeyCode D0 = Key.D0;
    public static readonly KeyCode Number1 = Key.Number1;
    public static readonly KeyCode Number2 = Key.Number2;
    public static readonly KeyCode Number3 = Key.Number3;
    public static readonly KeyCode Number4 = Key.Number4;
    public static readonly KeyCode Number5 = Key.Number5;
    public static readonly KeyCode Number6 = Key.Number6;
    public static readonly KeyCode Number7 = Key.Number7;   
    public static readonly KeyCode Number8 = Key.Number8;
    public static readonly KeyCode Number9 = Key.Number9;
    public static readonly KeyCode Semicolon = Key.Semicolon;
    public static readonly KeyCode Equal = Key.Equal;
    public static readonly KeyCode A = Key.A;
    public static readonly KeyCode B = Key.B;
    public static readonly KeyCode C = Key.C;
    public static readonly KeyCode D = Key.D;
    public static readonly KeyCode E = Key.E;
    public static readonly KeyCode F = Key.F;
    public static readonly KeyCode G = Key.G;
    public static readonly KeyCode H = Key.H;   
    public static readonly KeyCode I = Key.I;
    public static readonly KeyCode J = Key.J;
    public static readonly KeyCode K = Key.K;
    public static readonly KeyCode L = Key.L;
    public static readonly KeyCode M = Key.M;
    public static readonly KeyCode N = Key.N;
    public static readonly KeyCode O = Key.O;
    public static readonly KeyCode P = Key.P;
    public static readonly KeyCode Q = Key.Q;
    public static readonly KeyCode R = Key.R;
    public static readonly KeyCode S = Key.S;
    public static readonly KeyCode T = Key.T;
    public static readonly KeyCode U = Key.U;
    public static readonly KeyCode V = Key.V;
    public static readonly KeyCode W = Key.W;
    public static readonly KeyCode X = Key.X;
    public static readonly KeyCode Y = Key.Y;
    public static readonly KeyCode Z = Key.Z;
    public static readonly KeyCode LeftBracket = Key.LeftBracket;
    public static readonly KeyCode BackSlash = Key.BackSlash;
    public static readonly KeyCode RightBracket = Key.RightBracket;
    public static readonly KeyCode GraveAccent = Key.GraveAccent;
    public static readonly KeyCode World1 = Key.World1;
    public static readonly KeyCode World2 = Key.World2;
    public static readonly KeyCode Escape = Key.Escape;
    public static readonly KeyCode Enter = Key.Enter;
    public static readonly KeyCode Tab = Key.Tab;
    public static readonly KeyCode Backspace = Key.Backspace;
    public static readonly KeyCode Insert = Key.Insert;
    public static readonly KeyCode Delete = Key.Delete;
    public static readonly KeyCode Right = Key.Right;
    public static readonly KeyCode Left = Key.Left;
    public static readonly KeyCode Down = Key.Down;
    public static readonly KeyCode Up = Key.Up;
    public static readonly KeyCode PageUp = Key.PageUp;
    public static readonly KeyCode PageDown = Key.PageDown;
    public static readonly KeyCode Home = Key.Home;
    public static readonly KeyCode End = Key.End;
    public static readonly KeyCode CapsLock = Key.CapsLock;
    public static readonly KeyCode ScrollLock = Key.ScrollLock;
    public static readonly KeyCode NumLock = Key.NumLock;
    public static readonly KeyCode PrintScreen = Key.PrintScreen;
    public static readonly KeyCode Pause = Key.Pause;
    public static readonly KeyCode F1 = Key.F1;
    public static readonly KeyCode F2 = Key.F2;
    public static readonly KeyCode F3 = Key.F3;
    public static readonly KeyCode F4 = Key.F4;
    public static readonly KeyCode F5 = Key.F5;
    public static readonly KeyCode F6 = Key.F6;
    public static readonly KeyCode F7 = Key.F7;
    public static readonly KeyCode F8 = Key.F8;
    public static readonly KeyCode F9 = Key.F9;
    public static readonly KeyCode F10 = Key.F10;
    public static readonly KeyCode F11 = Key.F11;
    public static readonly KeyCode F12 = Key.F12;
    public static readonly KeyCode F13 = Key.F13;
    public static readonly KeyCode F14 = Key.F14;
    public static readonly KeyCode F15 = Key.F15;
    public static readonly KeyCode F16 = Key.F16;
    public static readonly KeyCode F17 = Key.F17;
    public static readonly KeyCode F18 = Key.F18;
    public static readonly KeyCode F19 = Key.F19;
    public static readonly KeyCode F20 = Key.F20;
    public static readonly KeyCode F21 = Key.F21;
    public static readonly KeyCode F22 = Key.F22;
    public static readonly KeyCode F23 = Key.F23;
    public static readonly KeyCode F24 = Key.F24;
    public static readonly KeyCode F25 = Key.F25;
    public static readonly KeyCode Keypad0 = Key.Keypad0;
    public static readonly KeyCode Keypad1 = Key.Keypad1;
    public static readonly KeyCode Keypad2 = Key.Keypad2;
    public static readonly KeyCode Keypad3 = Key.Keypad3;
    public static readonly KeyCode Keypad4 = Key.Keypad4;
    public static readonly KeyCode Keypad5 = Key.Keypad5;
    public static readonly KeyCode Keypad6 = Key.Keypad6;
    public static readonly KeyCode Keypad7 = Key.Keypad7;
    public static readonly KeyCode Keypad8 = Key.Keypad8;
    public static readonly KeyCode Keypad9 = Key.Keypad9;
    public static readonly KeyCode KeypadDecimal = Key.KeypadDecimal;
    public static readonly KeyCode KeypadDivide = Key.KeypadDivide;
    public static readonly KeyCode KeypadMultiply = Key.KeypadMultiply;
    public static readonly KeyCode KeypadSubtract = Key.KeypadSubtract;
    public static readonly KeyCode KeypadAdd = Key.KeypadAdd;
    public static readonly KeyCode KeypadEnter = Key.KeypadEnter;
    public static readonly KeyCode KeypadEqual = Key.KeypadEqual;
    public static readonly KeyCode ShiftLeft = Key.ShiftLeft;
    public static readonly KeyCode ControlLeft = Key.ControlLeft;
    public static readonly KeyCode AltLeft = Key.AltLeft;
    public static readonly KeyCode SuperLeft = Key.SuperLeft;
    public static readonly KeyCode ShiftRight = Key.ShiftRight;
    public static readonly KeyCode ControlRight = Key.ControlRight;
    public static readonly KeyCode AltRight = Key.AltRight;
    public static readonly KeyCode SuperRight = Key.SuperRight;
    public static readonly KeyCode Menu = Key.Menu;


    public KeyCode(int value)
    {
        this.value = value;
    }
    public readonly int value;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator Key(KeyCode key)
    {
        return (Key)key.value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator KeyCode(Key key)
    {
        return new KeyCode((int)key);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator int(KeyCode key)
    {
        return key.value;
    }
}