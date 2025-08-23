namespace Alco.Engine;

/// <summary>
/// Represents standardized gamepad buttons across different controllers.
/// The naming follows directional semantics for face buttons (South/East/West/North)
/// to align with industry conventions (Unity/Unreal), enabling consistent mapping
/// between Xbox (A/B/X/Y) and PlayStation (Cross/Circle/Square/Triangle).
/// </summary>
public enum GamepadButton
{
    /// <summary>
    /// Unknown or unmapped button.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// South face button (A on Xbox, Cross on PlayStation).
    /// </summary>
    South,
    /// <summary>
    /// East face button (B on Xbox, Circle on PlayStation).
    /// </summary>
    East,
    /// <summary>
    /// West face button (X on Xbox, Square on PlayStation).
    /// </summary>
    West,
    /// <summary>
    /// North face button (Y on Xbox, Triangle on PlayStation).
    /// </summary>
    North,

    /// <summary>
    /// Left shoulder button (LB / L1).
    /// </summary>
    LeftShoulder,
    /// <summary>
    /// Right shoulder button (RB / R1).
    /// </summary>
    RightShoulder,

    /// <summary>
    /// Left trigger as a digital press (LT / L2 thresholded).
    /// </summary>
    LeftTrigger,
    /// <summary>
    /// Right trigger as a digital press (RT / R2 thresholded).
    /// </summary>
    RightTrigger,

    /// <summary>
    /// Left stick press (L3).
    /// </summary>
    LeftStick,
    /// <summary>
    /// Right stick press (R3).
    /// </summary>
    RightStick,

    /// <summary>
    /// D-pad up.
    /// </summary>
    DPadUp,
    /// <summary>
    /// D-pad down.
    /// </summary>
    DPadDown,
    /// <summary>
    /// D-pad left.
    /// </summary>
    DPadLeft,
    /// <summary>
    /// D-pad right.
    /// </summary>
    DPadRight,

    /// <summary>
    /// Back / View / Select button.
    /// </summary>
    Back,
    /// <summary>
    /// Start / Menu / Options button.
    /// </summary>
    Start,
    /// <summary>
    /// Guide / Home / PS button.
    /// </summary>
    Guide,

    /// <summary>
    /// Touchpad press (common on DualShock/DualSense). Optional capability.
    /// </summary>
    Touchpad,
}

