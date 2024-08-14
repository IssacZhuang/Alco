using System.Numerics;

namespace Vocore.Engine;

/// <summary>
/// The input class for no mouse and keyboard input. Usually used for testing and server environments.
/// </summary>
public class NoInputSystem : InputSystem
{
    /// <inheritdoc />
    public override Vector2 MousePosition { get; set; }

    /// <inheritdoc />
    public override Vector2 MouseDelta { get; }

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
}