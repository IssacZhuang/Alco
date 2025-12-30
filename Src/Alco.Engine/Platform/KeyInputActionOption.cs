namespace Alco.Engine;

/// <summary>
/// Serializable options for configuring a <see cref="KeyInputAction"/>.
/// </summary>
public sealed class KeyInputActionOption
{
    /// <summary>
    /// Keyboard keys that trigger the action.
    /// </summary>
    public List<KeyCode> Keys { get; set; } = new();

    /// <summary>
    /// Gamepad buttons that trigger the action.
    /// </summary>
    public List<GamepadButton> GamepadButtons { get; set; } = new();

    /// <summary>
    /// Mouse buttons that trigger the action.
    /// </summary>
    public List<Mouse> MouseButtons { get; set; } = new();
}


