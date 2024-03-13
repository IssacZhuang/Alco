using System.Runtime.CompilerServices;
using Silk.NET.Input;

namespace Vocore.Engine;

/// <summary>
/// The mouse button <br/>
/// </summary>
public readonly struct Mouse
{
    //copy from Silk.NET.Input.MouseButton
    public static readonly Mouse Unknown = MouseButton.Unknown;
    public static readonly Mouse Left = MouseButton.Left;
    public static readonly Mouse Right = MouseButton.Right;
    public static readonly Mouse Middle = MouseButton.Middle;
    public static readonly Mouse Button4 = MouseButton.Button4;
    public static readonly Mouse Button5 = MouseButton.Button5;
    public static readonly Mouse Button6 = MouseButton.Button6;
    public static readonly Mouse Button7 = MouseButton.Button7;
    public static readonly Mouse Button8 = MouseButton.Button8;
    public static readonly Mouse Button9 = MouseButton.Button9;
    public static readonly Mouse Button10 = MouseButton.Button10;
    public static readonly Mouse Button11 = MouseButton.Button11;
    public static readonly Mouse Button12 = MouseButton.Button12;

    public readonly int value;
    public Mouse(int value)
    {
        this.value = value;
    }
    

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator MouseButton(Mouse button)
    {
        return (MouseButton)button.value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator Mouse(MouseButton button)
    {
        return new Mouse((int)button);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator int(Mouse button)
    {
        return button.value;
    }
}