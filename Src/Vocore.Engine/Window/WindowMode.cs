

using System.Runtime.CompilerServices;
using Silk.NET.Windowing;

namespace Vocore.Engine;



public struct WindowMode
{
    public static readonly WindowMode Normal = WindowState.Normal;
    public static readonly WindowMode Minimized = WindowState.Minimized;
    public static readonly WindowMode Maximized = WindowState.Maximized;
    public static readonly WindowMode Fullscreen = WindowState.Fullscreen;
    public int value;
    public WindowMode(int value)
    {
        this.value = value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator WindowMode(WindowState key)
    {
        return new WindowMode((int)key);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator WindowState(WindowMode key)
    {
        return (WindowState)key.value;
    }
}