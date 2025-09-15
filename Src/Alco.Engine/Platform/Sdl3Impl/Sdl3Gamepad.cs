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
    private const int MaxButtonCount = 32;

    private struct ButtonState
    {
        public fixed bool isDown[MaxButtonCount];
        public fixed bool isUp[MaxButtonCount];
        public fixed bool isPressing[MaxButtonCount];
    }

    private ButtonState _state;

    internal Sdl3Gamepad(SDL_Gamepad native)
    {
        _native = native;
        Name = SDL_GetGamepadName(_native) ?? "Unknown gamepad";
        GamepadType = ConvertGamepadType(SDL_GetGamepadType(_native));
    }

    /// <summary>
    /// Gets the device name reported by SDL.
    /// </summary>
    public override string Name {get;}

    /// <summary>
    /// Gets the gamepad type (Xbox, PlayStation, etc.) detected by SDL.
    /// </summary>
    public override GamepadType GamepadType { get; }

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

        return _state.isPressing[(int)button];
    }

    /// <summary>
    /// True if button transitioned to pressed state this frame.
    /// </summary>
    public override bool IsButtonDown(GamepadButton button)
    {
        if (!IsConnected) { return false; }
        return _state.isDown[(int)button];
    }

    /// <summary>
    /// True if button transitioned to released state this frame.
    /// </summary>
    public override bool IsButtonUp(GamepadButton button)
    {
        if (!IsConnected) { return false; }
        return _state.isUp[(int)button];
    }

    /// <summary>
    /// True if button is currently held down.
    /// </summary>
    public override bool IsButtonPressing(GamepadButton button)
    {
        if (!IsConnected) { return false; }
        return _state.isPressing[(int)button];
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
    /// Reset per-frame transient states for button down/up. Should be called once per frame.
    /// </summary>
    internal void ResetFrame()
    {
        for (int i = 0; i < MaxButtonCount; i++)
        {
            _state.isDown[i] = false;
            _state.isUp[i] = false;
        }
    }

    /// <summary>
    /// Mark a button as pressed (edge and level states).
    /// </summary>
    internal void RecordButtonDown(GamepadButton button)
    {
        int i = (int)button;
        _state.isDown[i] = true;
        _state.isPressing[i] = true;
        DoButtonDown(button);
    }

    /// <summary>
    /// Mark a button as released (edge and level states).
    /// </summary>
    internal void RecordButtonUp(GamepadButton button)
    {
        int i = (int)button;
        _state.isUp[i] = true;
        _state.isPressing[i] = false;
        DoButtonUp(button);
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

    /// <summary>
    /// Converts SDL button enum to engine button enum.
    /// </summary>
    public static GamepadButton ConvertButton(SDL_GamepadButton button)
    {
        return button switch
        {
            SDL_GamepadButton.South => GamepadButton.South,
            SDL_GamepadButton.East => GamepadButton.East,
            SDL_GamepadButton.West => GamepadButton.West,
            SDL_GamepadButton.North => GamepadButton.North,
            SDL_GamepadButton.Back => GamepadButton.Back,
            SDL_GamepadButton.Guide => GamepadButton.Guide,
            SDL_GamepadButton.Start => GamepadButton.Start,
            SDL_GamepadButton.LeftStick => GamepadButton.LeftStick,
            SDL_GamepadButton.RightStick => GamepadButton.RightStick,
            SDL_GamepadButton.LeftShoulder => GamepadButton.LeftShoulder,
            SDL_GamepadButton.RightShoulder => GamepadButton.RightShoulder,
            SDL_GamepadButton.DpadUp => GamepadButton.DPadUp,
            SDL_GamepadButton.DpadDown => GamepadButton.DPadDown,
            SDL_GamepadButton.DpadLeft => GamepadButton.DPadLeft,
            SDL_GamepadButton.DpadRight => GamepadButton.DPadRight,
            SDL_GamepadButton.Touchpad => GamepadButton.Touchpad,
            _ => GamepadButton.Unknown,
        };
    }

    /// <summary>
    /// Converts SDL gamepad type to engine gamepad type.
    /// </summary>
    private static GamepadType ConvertGamepadType(SDL_GamepadType sdlType)
    {
        return sdlType switch
        {
            SDL_GamepadType.Unknown => GamepadType.Unknown,
            SDL_GamepadType.Standard => GamepadType.Standard,
            SDL_GamepadType.Xbox360 => GamepadType.Xbox360,
            SDL_GamepadType.Xboxone => GamepadType.XboxOne,
            SDL_GamepadType.Ps3 => GamepadType.PlayStation3,
            SDL_GamepadType.Ps4 => GamepadType.PlayStation4,
            SDL_GamepadType.Ps5 => GamepadType.PlayStation5,
            SDL_GamepadType.NintendoSwitchPro => GamepadType.NintendoSwitchPro,
            SDL_GamepadType.NintendoSwitchJoyconLeft => GamepadType.NintendoSwitchJoyconLeft,
            SDL_GamepadType.NintendoSwitchJoyconRight => GamepadType.NintendoSwitchJoyconRight,
            SDL_GamepadType.NintendoSwitchJoyconPair => GamepadType.NintendoSwitchJoyconPair,
            _ => GamepadType.Unknown,
        };
    }
}