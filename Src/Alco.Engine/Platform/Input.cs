using System;
using System.Numerics;

namespace Alco.Engine;

/// <summary>
/// Represents an abstract base class for input handling.
/// </summary>
public abstract class Input
{
    private readonly WeakEvent<KeyCode> _onKeyDown = new();
    private readonly WeakEvent<KeyCode> _onKeyUp = new();
    private readonly WeakEvent<Mouse> _onMouseDown = new();
    private readonly WeakEvent<Mouse> _onMouseUp = new();
    private readonly WeakEvent<Gamepad> _onGamepadConnected = new();
    private readonly WeakEvent<Gamepad> _onGamepadDisconnected = new();

    /// <summary>
    /// Occurs when a key is pressed down.
    /// </summary>
    public event Action<KeyCode> OnKeyDown
    {
        add => _onKeyDown.AddListener(value);
        remove => _onKeyDown.RemoveListener(value);
    }

    /// <summary>
    /// Occurs when a key is released.
    /// </summary>
    public event Action<KeyCode> OnKeyUp
    {
        add => _onKeyUp.AddListener(value);
        remove => _onKeyUp.RemoveListener(value);
    }

    /// <summary>
    /// Occurs when a mouse button is pressed down.
    /// </summary>
    public event Action<Mouse> OnMouseDown
    {
        add => _onMouseDown.AddListener(value);
        remove => _onMouseDown.RemoveListener(value);
    }

    /// <summary>
    /// Occurs when a mouse button is released.
    /// </summary>
    public event Action<Mouse> OnMouseUp
    {
        add => _onMouseUp.AddListener(value);
        remove => _onMouseUp.RemoveListener(value);
    }

    /// <summary>
    /// Occurs when a gamepad is connected.
    /// </summary>
    public event Action<Gamepad> OnGamepadConnected
    {
        add => _onGamepadConnected.AddListener(value);
        remove => _onGamepadConnected.RemoveListener(value);
    }

    /// <summary>
    /// Occurs when a gamepad is disconnected.
    /// </summary>
    public event Action<Gamepad> OnGamepadDisconnected
    {
        add => _onGamepadDisconnected.AddListener(value);
        remove => _onGamepadDisconnected.RemoveListener(value);
    }

    /// <summary>
    /// Gets or sets the position of the mouse. The zero of coordinate system is top-left 
    /// <br/>[Attention] This is the global mouse position relative to the screen,
    /// use <see cref="View.MousePosition"/> if you want the local position in the window.
    /// </summary>
    public abstract Vector2 MousePosition { get; set; }

    /// <summary>
    /// Gets the delta movement of the mouse.
    /// </summary>
    public abstract Vector2 MouseDelta { get; }

    /// <summary>
    /// Gets the delta movement of the mouse wheel.
    /// </summary>
    public abstract float MouseWheelDelta { get; }

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
    /// Determines whether the mouse wheel is currently scrolling.
    /// </summary>
    /// <param name="delta">The delta scroll value.</param>
    /// <returns><c>true</c> if the mouse wheel is currently scrolling; otherwise, <c>false</c>.</returns>
    public abstract bool IsMouseWheelScrolling(out float delta);

    /// <summary>
    /// Copy the specified text to the clipboard.
    /// </summary>
    /// <param name="text"></param>
    public abstract void CopyToClipboard(ReadOnlySpan<char> text);

    /// <summary>
    /// Gets the text from the clipboard.
    /// </summary>
    public abstract ReadOnlySpan<char> GetClipboardText();

    public abstract IReadOnlyList<Gamepad> GetGamepads();

    public Gamepad? PrimaryGamepad
    {
        get
        {
            var gamepads = GetGamepads();
            return gamepads.Count > 0 ? gamepads[0] : null;
        }
    }

    protected void DoKeyDown(KeyCode key)
    {
        _onKeyDown.Invoke(key);
    }

    protected void DoKeyUp(KeyCode key)
    {
        _onKeyUp.Invoke(key);
    }

    protected void DoMouseDown(Mouse button)
    {
        _onMouseDown.Invoke(button);
    }

    protected void DoMouseUp(Mouse button)
    {
        _onMouseUp.Invoke(button);
    }

    protected void DoGamepadConnected(Gamepad gamepad)
    {
        _onGamepadConnected.Invoke(gamepad);
    }

    protected void DoGamepadDisconnected(Gamepad gamepad)
    {
        _onGamepadDisconnected.Invoke(gamepad);
    }
}