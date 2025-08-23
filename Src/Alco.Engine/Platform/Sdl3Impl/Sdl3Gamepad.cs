using SDL3;

using static SDL3.SDL3;

namespace Alco.Engine;

public unsafe class Sdl3Gamepad : Gamepad
{
    private readonly SDL_Gamepad _native;

    internal Sdl3Gamepad(SDL_Gamepad native)
    {
        _native = native;
        Name = SDL_GetGamepadName(_native) ?? "Unknown gamepad";
    }


    public override string Name {get;}

    public override bool IsConnected => SDL_GamepadConnected(_native);

    public override float GetAxis(GamepadAxis axis)
    {
        if(!IsConnected)
        {
            return 0;
        }

        short value = SDL_GetGamepadAxis(_native, ConvertAxis(axis));
        return (float)value / short.MaxValue;
    }

    public override bool IsButtonPressed(GamepadButton button)
    {
        if(!IsConnected)
        {
            return false;
        }

        return SDL_GetGamepadButton(_native, ConvertButton(button));
    }

    public override void SetVibration(float intensity, float duration)
    {
        if(!IsConnected)
        {
            return;
        }

        uint durationMs = (uint)(duration * 1000);
        ushort intensity16 = (ushort)(intensity * ushort.MaxValue);
        SDL_RumbleGamepad(_native, intensity16, intensity16, durationMs);
    }

    protected override void Dispose(bool disposing)
    {

    }

    public static SDL_GamepadAxis ConvertAxis(GamepadAxis axis)
    {
        return axis switch
        {
            GamepadAxis.LeftX => SDL_GamepadAxis.Leftx,
            GamepadAxis.LeftY => SDL_GamepadAxis.Lefty,
            GamepadAxis.RightX => SDL_GamepadAxis.Rightx,
            GamepadAxis.RightY => SDL_GamepadAxis.Righty,
            GamepadAxis.LeftTrigger => SDL_GamepadAxis.LeftTrigger,
            GamepadAxis.RightTrigger => SDL_GamepadAxis.RightTrigger,
            _ => throw new ArgumentException($"Invalid gamepad axis: {axis}"),
        };
    }

    public static SDL_GamepadButton ConvertButton(GamepadButton button){
        return button switch
        {
            GamepadButton.South => SDL_GamepadButton.South,
            GamepadButton.East => SDL_GamepadButton.East,
            GamepadButton.West => SDL_GamepadButton.West,
            GamepadButton.North => SDL_GamepadButton.North,
            GamepadButton.Back => SDL_GamepadButton.Back,
            GamepadButton.Guide => SDL_GamepadButton.Guide,
            GamepadButton.Start => SDL_GamepadButton.Start,
            GamepadButton.LeftStick => SDL_GamepadButton.LeftStick,
            GamepadButton.RightStick => SDL_GamepadButton.RightStick,
            GamepadButton.LeftShoulder => SDL_GamepadButton.LeftShoulder,
            GamepadButton.RightShoulder => SDL_GamepadButton.RightShoulder,
            GamepadButton.DPadUp => SDL_GamepadButton.DpadUp,
            GamepadButton.DPadDown => SDL_GamepadButton.DpadDown,
            GamepadButton.DPadLeft => SDL_GamepadButton.DpadLeft,
            GamepadButton.DPadRight => SDL_GamepadButton.DpadRight,
            GamepadButton.Touchpad => SDL_GamepadButton.Touchpad,
            _ => throw new ArgumentException($"Invalid gamepad button: {button}"),
        };
    }
}
