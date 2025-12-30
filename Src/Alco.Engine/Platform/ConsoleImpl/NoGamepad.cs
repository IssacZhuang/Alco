
namespace Alco.Engine;

public class NoGamepad : Gamepad
{
    public override string Name => "No Gamepad";

    public override bool IsConnected => false;

    public override GamepadType GamepadType => GamepadType.Unknown;

    public override float GetAxis(GamepadAxis axis)
    {
        return 0;
    }

    public override bool IsButtonPressed(GamepadButton button)
    {
        return false;
    }

    public override void SetVibration(float intensity, float duration)
    {
        
    }
}