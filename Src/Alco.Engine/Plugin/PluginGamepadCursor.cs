using System;
using System.Numerics;

namespace Alco.Engine;

/// <summary>
/// Engine plugin that maps the primary gamepad right stick to the system mouse cursor.
/// This runs as an engine system and updates every frame, clamping the cursor to the main view size.
/// </summary>
public sealed class PluginGamepadCursor : BaseEnginePlugin
{
    

    private sealed class GamepadCursorSystem : BaseEngineSystem
    {
        public const float VelocityMultiplier = 800f;
        public const float ScreenHeightMultiplier = 1 / 1080f;

        private readonly Input _input;
        private readonly View _view;

        // dead zone to avoid drift
        public float DeadZone { get; set; } = 0.1f;
        public float Sensitivity { get; set; } = 1.0f;

        private Vector2 _pixelAccumulator;
        private Func<Vector2, Vector2> _curve = AxisInputAction.CurveQuadratic;

        public GamepadCursorSystem(Input input, View view, float deadZone, float sensitivity)
        {
            _input = input;
            _view = view;
            DeadZone = deadZone;
            Sensitivity = sensitivity;
        }

        /// <summary>
        /// Update cursor position each frame using right stick.
        /// </summary>
        public override void OnUpdate(float delta)
        {
            Gamepad? gamepad = _input.PrimaryGamepad;
            if (gamepad == null || !gamepad.IsConnected)
            {
                return;
            }

            float rx = gamepad.GetAxis(GamepadAxis.RightX);
            float ry = gamepad.GetAxis(GamepadAxis.RightY);

            // Apply dead zone
            if (MathF.Abs(rx) < DeadZone) { rx = 0f; }
            if (MathF.Abs(ry) < DeadZone) { ry = 0f; }

            if (rx == 0f && ry == 0f)
            {
                return;
            }

            // Convert stick to pixel delta. View coordinates are top-left origin with Y+ down.
            // SDL right stick Y is negative when pushed up. Using ry directly makes up (ry<0)
            // produce a negative delta Y, which moves the cursor up as expected.
            float speed = MathF.Max(0f, Sensitivity);
            // RightY at the SDL layer is normalized to Y+ up; screen-space is Y+ down, so invert here
            Vector2 axis = new Vector2(rx, -ry);
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

    /// <summary>
    /// Run after ImGUI so it can coexist; ordering can be adjusted if needed.
    /// </summary>
    public override int Order => 2150;

    /// <summary>
    /// Gets or sets the dead zone threshold for gamepad axis input to avoid drift.
    /// Values below this threshold are treated as zero. Default is 0.1.
    /// </summary>
    public float DeadZone { get; set; } = 0.1f;

    /// <summary>
    /// Gets or sets the sensitivity multiplier for cursor movement speed.
    /// Higher values result in faster cursor movement. Default is 1.0.
    /// </summary>
    public float Sensitivity { get; set; } = 1.0f;

    /// <summary>
    /// Install the gamepad cursor system into the engine.
    /// </summary>
    /// <param name="engine">Engine instance.</param>
    public override void OnPostInitialize(GameEngine engine)
    {
        var system = new GamepadCursorSystem(engine.Input, engine.MainView, DeadZone, Sensitivity);
        engine.AddSystem(system);
    }
}


