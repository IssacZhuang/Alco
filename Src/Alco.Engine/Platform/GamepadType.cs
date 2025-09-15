namespace Alco.Engine;

/// <summary>
/// Represents the type of gamepad controller.
/// </summary>
public enum GamepadType
{
    /// <summary>
    /// Unknown or unrecognized gamepad type.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// Standard/Generic gamepad.
    /// </summary>
    Standard = 1,

    /// <summary>
    /// Xbox 360 controller.
    /// </summary>
    Xbox360 = 2,

    /// <summary>
    /// Xbox One controller.
    /// </summary>
    XboxOne = 3,

    /// <summary>
    /// PlayStation 3 controller (DualShock 3).
    /// </summary>
    PlayStation3 = 4,

    /// <summary>
    /// PlayStation 4 controller (DualShock 4).
    /// </summary>
    PlayStation4 = 5,

    /// <summary>
    /// PlayStation 5 controller (DualSense).
    /// </summary>
    PlayStation5 = 6,

    /// <summary>
    /// Nintendo Switch Pro controller.
    /// </summary>
    NintendoSwitchPro = 7,

    /// <summary>
    /// Nintendo Switch Joy-Con Left.
    /// </summary>
    NintendoSwitchJoyconLeft = 8,

    /// <summary>
    /// Nintendo Switch Joy-Con Right.
    /// </summary>
    NintendoSwitchJoyconRight = 9,

    /// <summary>
    /// Nintendo Switch Joy-Con Pair.
    /// </summary>
    NintendoSwitchJoyconPair = 10,
}