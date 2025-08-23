using SDL3;

using static SDL3.SDL3;

namespace Alco.Engine;

/// <summary>
/// SDL3-backed implementation of <see cref="Gamepad"/>.
/// Ensures safe queries when disconnected and releases the native handle on cleanup.
/// </summary>
public unsafe class Sdl3Gamepad : Gamepad
{
    private readonly SDL_Gamepad _native;

    internal Sdl3Gamepad(SDL_Gamepad native)
    {
        _native = native;
        Name = SDL_GetGamepadName(_native) ?? "Unknown gamepad";
    }


    /// <summary>
    /// Gets the device name reported by SDL.
    /// </summary>
    public override string Name {get;}

    /// <summary>
    /// Indicates whether the device is currently connected.
    /// </summary>
    public override bool IsConnected =>  SDL_GamepadConnected(_native);

    /// <summary>
    /// Gets the normalized axis value in range [-1, 1] (triggers [0, 1]).
    /// Returns 0 when disconnected.
    /// </summary>
    public override float GetAxis(GamepadAxis axis)
    {
        if(!IsConnected)
        {
            return 0;
        }

        short value = SDL_GetGamepadAxis(_native, ConvertAxis(axis));
        return (float)value / short.MaxValue;
    }

    /// <summary>
    /// Returns whether the specified button is pressed. False when disconnected.
    /// </summary>
    public override bool IsButtonPressed(GamepadButton button)
    {
        if(!IsConnected)
        {
            return false;
        }

        return SDL_GetGamepadButton(_native, ConvertButton(button));
    }

    /// <summary>
    /// Triggers simple vibration with given intensity [0,1] and duration (seconds).
    /// Does nothing when disconnected.
    /// </summary>
    /// <param name="intensity">Strength in [0,1].</param>
    /// <param name="duration">Duration in seconds.</param>
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

    internal void CleanUp()
    {
        SDL_CloseGamepad(_native);
    }

    /// <summary>
    /// Converts engine axis enum to SDL axis.
    /// </summary>
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

    /// <summary>
    /// Converts engine button enum to SDL button.
    /// </summary>
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
