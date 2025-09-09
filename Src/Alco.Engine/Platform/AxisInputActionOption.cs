using System.Numerics;

namespace Alco.Engine;

/// <summary>
/// Serializable options for configuring an <see cref="AxisInputAction"/>.
/// </summary>
public sealed class AxisInputActionOption
{
    /// <summary>
    /// Mapping from keyboard <see cref="KeyCode"/> to its directional contribution.
    /// </summary>
    public Dictionary<KeyCode, Vector2> Keys { get; set; } = new();

    /// <summary>
    /// Mapping from <see cref="GamepadAxis"/> to its directional contribution.
    /// </summary>
    public Dictionary<GamepadAxis, Vector2> GamepadAxes { get; set; } = new();

    /// <summary>
    /// Radial deadzone applied to the aggregated gamepad axis vector. Range: [0, 1].
    /// Values whose length is less than or equal to this threshold are treated as zero.
    /// </summary>
    public float Deadzone { get; set; } = 0.15f;
}