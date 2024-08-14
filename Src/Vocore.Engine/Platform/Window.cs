using System.Numerics;
using System.Runtime.CompilerServices;
using Silk.NET.OpenAL;
using Vocore.Graphics;

namespace Vocore.Engine;

/// <summary>
/// Represents a window in the application.
/// </summary>
public abstract class Window : AutoDisposable//todo : change to disposable
{
    /// <summary>
    /// Gets or sets the window mode.
    /// </summary>
    public abstract WindowMode WindowMode { get; set; }

    /// <summary>
    /// Gets or sets the position of the window.
    /// </summary> 
    public abstract int2 Position { get; set; }

    /// <summary>
    /// Gets or sets the size of the window.
    /// </summary>
    public abstract uint2 Size { get; set; }

    /// <summary>
    /// Gets or sets the title of the window.
    /// </summary>
    public abstract string Title { get; set; }

    public abstract GPUSwapchain? Swapchain { get; }

    public Action<uint2>? OnResize;

    public abstract void Close();


    /// <summary>
    /// Gets the aspect ratio of the window.
    /// </summary>
    public float AspectRatio
    {
        get => (float)Size.x / Size.y;
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Vector2 GetLocalMousePosition(Vector2 globalMousePosition)
    {
        return new Vector2(globalMousePosition.X - Position.x, globalMousePosition.Y - Position.y);
    }

}