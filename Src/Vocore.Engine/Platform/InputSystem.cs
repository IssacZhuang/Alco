using System.Numerics;

namespace Vocore.Engine;

/// <summary>
/// Represents an abstract base class for input handling.
/// </summary>
public abstract class InputSystem
{
    /// <summary>
    /// Gets or sets the position of the mouse.
    /// </summary>
    public abstract Vector2 MousePosition { get; set; }

    /// <summary>
    /// Gets the delta movement of the mouse.
    /// </summary>
    public abstract Vector2 MouseDelta { get; }

    /// <summary>
    /// Determines whether the specified key is currently being pressed down.
    /// </summary>
    /// <param name="key">The key to check.</param>
    /// <returns><c>true</c> if the key is currently being pressed down; otherwise, <c>false</c>.</returns>
    public abstract bool IsKeyDown(KeyCode key);

    /// <summary>
    /// Determines whether the specified key is currently released (not pressed down).
    /// </summary>
    /// <param name="key">The key to check.</param>
    /// <returns><c>true</c> if the key is currently released; otherwise, <c>false</c>.</returns>
    public abstract bool IsKeyUp(KeyCode key);

    /// <summary>
    /// Determines whether the specified key is currently being pressed.
    /// </summary>
    /// <param name="key">The key to check.</param>
    /// <returns><c>true</c> if the key is currently being pressed; otherwise, <c>false</c>.</returns>
    public abstract bool IsKeyPressing(KeyCode key);

    /// <summary>
    /// Determines whether the specified mouse button is currently being pressed down.
    /// </summary>
    /// <param name="button">The mouse button to check.</param>
    /// <returns><c>true</c> if the mouse button is currently being pressed down; otherwise, <c>false</c>.</returns>
    public abstract bool IsMouseDown(Mouse button);

    /// <summary>
    /// Determines whether the specified mouse button is currently released (not pressed down).
    /// </summary>
    /// <param name="button">The mouse button to check.</param>
    /// <returns><c>true</c> if the mouse button is currently released; otherwise, <c>false</c>.</returns>
    public abstract bool IsMouseUp(Mouse button);

    /// <summary>
    /// Determines whether the specified mouse button is currently being pressed.
    /// </summary>
    /// <param name="button">The mouse button to check.</param>
    /// <returns><c>true</c> if the mouse button is currently being pressed; otherwise, <c>false</c>.</returns>
    public abstract bool IsMousePressing(Mouse button);


    /// <summary>
    /// Determines whether the mouse is currently scrolling.
    /// </summary>
    /// <param name="delta">The delta scroll value.</param>
    /// <returns><c>true</c> if the mouse is currently scrolling; otherwise, <c>false</c>.</returns>
    public abstract bool IsMouseScrolling(out Vector2 delta);

    /// <summary>
    /// Updates the input state.
    /// </summary>
    internal abstract void Update();

    /// <summary>
    /// Processes input events.
    /// </summary>
    internal abstract void DoEvent();

    /// <summary>
    /// Resets the input state.
    /// </summary>
    internal abstract void Reset();
}