using System.Numerics;
using Alco;

namespace Alco.Engine;

public abstract class Gamepad
{
    public abstract string Name { get; }


    public abstract bool IsConnected { get; }

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

    public abstract float GetAxis(GamepadAxis axis);

    public Vector2 GetLeftStick()
    {
        return new Vector2(GetAxis(GamepadAxis.LeftX), GetAxis(GamepadAxis.LeftY));
    }

    public Vector2 GetRightStick()
    {
        return new Vector2(GetAxis(GamepadAxis.RightX), GetAxis(GamepadAxis.RightY));
    }

    public abstract void SetVibration(float intensity, float duration);
}