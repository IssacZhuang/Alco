using System.Numerics;

namespace Alco.Engine;

public abstract class Gamepad
{
    public abstract string Name { get; }


    public abstract bool IsConnected { get; }

    public abstract bool IsButtonPressed(GamepadButton button);

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