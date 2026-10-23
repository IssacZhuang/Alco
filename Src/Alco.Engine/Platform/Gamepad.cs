using System.Numerics;
using Alco;

namespace Alco.Engine;

/// <summary>
/// Represents a gamepad device and exposes its buttons, axes, and vibration control.
/// </summary>
public abstract class Gamepad
{
    /// <summary>
    /// Gets the human-readable name of the gamepad.
    /// </summary>
    public abstract string Name { get; }

    /// <summary>
    /// Gets the type of gamepad (Xbox, PlayStation, etc.).
    /// </summary>
    public abstract GamepadType GamepadType { get; }

    /// <summary>
    /// Gets whether the gamepad is currently connected.
    /// </summary>
    public abstract bool IsConnected { get; }

    /// <summary>
    /// Gets whether the specified button is currently pressed.
    /// </summary>
    /// <param name="button">The button to query.</param>
    /// <returns>True if the button is pressed; otherwise false.</returns>
    public abstract bool IsButtonPressed(GamepadButton button);

    /// <summary>
    /// Determines if the specified button transitioned to pressed state during the current frame.
    /// Default implementation returns false; platform-specific implementations may override.
    /// </summary>
    public virtual bool IsButtonDown(GamepadButton button) => false;

    /// <summary>
    /// Determines if the specified button transitioned to released state during the current frame.
    /// Default implementation returns false; platform-specific implementations may override.
    /// </summary>
    public virtual bool IsButtonUp(GamepadButton button) => false;

    /// <summary>
    /// Determines if the specified button is currently being held down.
    /// Default implementation falls back to <see cref="IsButtonPressed"/>.
    /// </summary>
    public virtual bool IsButtonPressing(GamepadButton button) => IsButtonPressed(button);

    private readonly WeakEvent<GamepadButton> _onButtonDown = new();
    private readonly WeakEvent<GamepadButton> _onButtonUp = new();

    /// <summary>
    /// Occurs when a gamepad button is pressed down.
    /// </summary>
    public event Action<GamepadButton> OnButtonDown
    {
        add => _onButtonDown.AddListener(value);
        remove => _onButtonDown.RemoveListener(value);
    }

    /// <summary>
    /// Occurs when a gamepad button is released.
    /// </summary>
    public event Action<GamepadButton> OnButtonUp
    {
        add => _onButtonUp.AddListener(value);
        remove => _onButtonUp.RemoveListener(value);
    }

    /// <summary>
    /// Raise the button down event for subscribers.
    /// </summary>
    /// <param name="button">The button that changed state.</param>
    protected void DoButtonDown(GamepadButton button)
    {
        _onButtonDown.Invoke(button);
    }

    /// <summary>
    /// Raise the button up event for subscribers.
    /// </summary>
    /// <param name="button">The button that changed state.</param>
    protected void DoButtonUp(GamepadButton button)
    {
        _onButtonUp.Invoke(button);
    }

    /// <summary>
    /// Gets the current value of the specified axis.
    /// </summary>
    /// <param name="axis">The axis to read.</param>
    /// <returns>The axis value, normally in the range [-1, 1].</returns>
    public abstract float GetAxis(GamepadAxis axis);

    /// <summary>
    /// Gets the left stick position as a vector.
    /// </summary>
    /// <returns>The left stick X and Y axis values.</returns>
    public Vector2 GetLeftStick()
    {
        return new Vector2(GetAxis(GamepadAxis.LeftX), GetAxis(GamepadAxis.LeftY));
    }

    /// <summary>
    /// Gets the right stick position as a vector.
    /// </summary>
    /// <returns>The right stick X and Y axis values.</returns>
    public Vector2 GetRightStick()
    {
        return new Vector2(GetAxis(GamepadAxis.RightX), GetAxis(GamepadAxis.RightY));
    }

    /// <summary>
    /// Starts vibration for the specified duration.
    /// </summary>
    /// <param name="intensity">Vibration intensity, normally in the range [0, 1].</param>
    /// <param name="duration">Duration in seconds.</param>
    public abstract void SetVibration(float intensity, float duration);
}