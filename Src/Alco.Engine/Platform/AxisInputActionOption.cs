using System.Numerics;

namespace Alco.Engine;

public sealed class AxisInputActionOption
{
    public Dictionary<KeyCode, Vector2> Keys { get; set; } = new();
    public Dictionary<GamepadAxis, Vector2> GamepadAxes { get; set; } = new();
}