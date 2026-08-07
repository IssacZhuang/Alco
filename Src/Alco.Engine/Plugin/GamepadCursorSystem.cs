using System;
using System.Numerics;

namespace Alco.Engine;

/// <summary>
/// Engine system that maps a gamepad stick to the system mouse cursor with velocity-based
/// movement. Which stick drives the cursor is selected by <see cref="IsLeftStickCursor"/>;
/// whether the system accumulates cursor motion at all is gated by <see cref="IsEnabled"/>.
/// The cursor is clamped to the main view size and written via <see cref="Input.MousePosition"/>.
/// </summary>
public sealed class GamepadCursorSystem : BaseEngineSystem
{
    public const float VelocityMultiplier = 1600f;
    public const float ScreenHeightMultiplier = 1 / 1080f;

    private readonly Input _input;
    private readonly View _view;

    // dead zone to avoid drift
    public float DeadZone { get; set; } = 0.1f;
    public float Sensitivity { get; set; } = 1.0f;

    private Vector2 _pixelAccumulator;
    private Func<Vector2, Vector2> _curve = AxisInputAction.CurveQuadratic;

    /// <summary>
    /// When <see langword="true"/> (default), this system reads the selected stick, accumulates
    /// cursor motion, and writes <see cref="Input.MousePosition"/> each frame (free-cursor mode,
    /// used for UI/Overworld/in-map aiming). When <see langword="false"/>, <see cref="OnUpdate"/>
    /// returns early so the game layer can drive the cursor position directly (e.g. in-map snap
    /// to a fixed offset from the player). The game layer (centralized owner) writes this each
    /// frame; <see cref="GamepadCursorSystem"/> only reads it.
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Selects which gamepad stick drives the cursor while <see cref="IsEnabled"/> is
    /// <see langword="true"/>. <see langword="false"/> (default) = right stick (in-map aiming);
    /// <see langword="true"/> = left stick (UI menus / Overworld).
    /// </summary>
    public bool IsLeftStickCursor { get; set; } = false;

    public GamepadCursorSystem(Input input, View view, float deadZone, float sensitivity)
    {
        _input = input;
        _view = view;
        DeadZone = deadZone;
        Sensitivity = sensitivity;
    }

    /// <summary>
    /// Update cursor position each frame using the selected stick, unless the game layer has
    /// taken over the cursor (<see cref="IsEnabled"/> == false).
    /// </summary>
    public override void OnUpdate(float delta)
    {
        // Game layer is driving the cursor this frame (in-map fixed-distance snap). Skip
        // free-cursor accumulation entirely so it does not fight the snap position.
        if (!IsEnabled)
        {
            return;
        }

        Gamepad? gamepad = _input.PrimaryGamepad;
        if (gamepad == null || !gamepad.IsConnected)
        {
            return;
        }

        float sx = gamepad.GetAxis(IsLeftStickCursor ? GamepadAxis.LeftX : GamepadAxis.RightX);
        float sy = gamepad.GetAxis(IsLeftStickCursor ? GamepadAxis.LeftY : GamepadAxis.RightY);

        // Apply dead zone
        if (MathF.Abs(sx) < DeadZone) { sx = 0f; }
        if (MathF.Abs(sy) < DeadZone) { sy = 0f; }

        if (sx == 0f && sy == 0f)
        {
            return;
        }

        // Convert stick to pixel delta. View coordinates are top-left origin with Y+ down.
        // SDL right stick Y is negative when pushed up. Using sy directly makes up (sy<0)
        // produce a negative delta Y, which moves the cursor up as expected.
        float speed = MathF.Max(0f, Sensitivity);
        // Stick Y at the SDL layer is normalized to Y+ up; screen-space is Y+ down, so invert here
        Vector2 axis = new Vector2(sx, -sy);
        // Apply response curve (quadratic by default)
        axis = _curve(axis);
        Vector2 velocity = axis * speed * VelocityMultiplier * ScreenHeightMultiplier * _view.Size.Y;
        Vector2 deltaPixels = velocity * delta;

        // Accumulate sub-pixel movement and apply whole-pixel steps only
        _pixelAccumulator += deltaPixels;
        int moveX = (int)_pixelAccumulator.X;
        int moveY = (int)_pixelAccumulator.Y;

        if (moveX == 0 && moveY == 0)
        {
            return;
        }

        _pixelAccumulator.X -= moveX;
        _pixelAccumulator.Y -= moveY;

        // Current local mouse position in the window
        Vector2 localPos = _view.MousePosition;
        Vector2 newLocal = localPos + new Vector2(moveX, moveY);

        // Clamp within window bounds [0, Size)
        uint2 size = _view.Size;
        newLocal.X = MathF.Min(MathF.Max(newLocal.X, 0f), size.X - 1);
        newLocal.Y = MathF.Min(MathF.Max(newLocal.Y, 0f), size.Y - 1);

        // Convert local (window) coords to global screen coords for Input.MousePosition setter
        int2 windowPos = _view.Position;
        Vector2 global = new Vector2(windowPos.X + newLocal.X, windowPos.Y + newLocal.Y);

        _input.MousePosition = global;
    }
}
