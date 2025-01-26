using System.Numerics;

namespace Alco.Engine;

/// <summary>
/// The input class for no mouse and keyboard input. Usually used for testing and server environments.
/// </summary>
public class NoInputSystem : InputSystem
{
    /// <inheritdoc />
    public override Vector2 MousePosition { get; set; }

    /// <inheritdoc />
    public override Vector2 MouseDelta { get; }

    public override float MouseWheelDelta { get; }

    public override void CopyToClipboard(ReadOnlySpan<char> text)
    {
        
    }

    public override ReadOnlySpan<char> GetClipboardText()
    {
        return ReadOnlySpan<char>.Empty;
    }

    /// <inheritdoc />
    public override bool IsKeyDown(KeyCode key)
    {
        return false;
    }

    /// <inheritdoc />
    public override bool IsKeyPressing(KeyCode key)
    {
        return false;
    }

    /// <inheritdoc />
    public override bool IsKeyUp(KeyCode key)
    {
        return true;
    }

    /// <inheritdoc />
    public override bool IsMouseDown(Mouse button)
    {
        return false;
    }

    /// <inheritdoc />
    public override bool IsMousePressing(Mouse button)
    {
        return false;
    }

    /// <inheritdoc />
    public override bool IsMouseScrolling(out Vector2 delta)
    {
        delta = Vector2.Zero;
        return false;
    }

    /// <inheritdoc />
    public override bool IsMouseUp(Mouse button)
    {
        return true;
    }

    public override bool IsMouseWheelScrolling(out float delta)
    {
        delta = 0;
        return false;
    }
}