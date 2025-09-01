namespace Alco.Engine;

public sealed class KeyInputActionOption
{
    public List<KeyCode> Keys { get; set; } = new();
    public List<GamepadButton> GamepadButtons { get; set; } = new();
}


