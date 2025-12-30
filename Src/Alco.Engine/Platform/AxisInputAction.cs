using System.Numerics;

namespace Alco.Engine;

/// <summary>
/// Aggregates directional input from keyboard and gamepad axes, with optional deadzone.
/// </summary>
public sealed class AxisInputAction
{
    private readonly Input _input;
    private readonly Gamepad? _gamepad;

    private readonly Dictionary<KeyCode, Vector2> _keys = new();
    private readonly Dictionary<GamepadAxis, Vector2> _gamepadAxes = new();

    private Func<Vector2, Vector2>? _curve;

    /// <summary>
    /// Applies a magnitude-only curve while preserving direction. Only affects inputs in [0, 1].
    /// Inputs with magnitude &gt; 1 are returned unchanged.
    /// </summary>
    private static Vector2 ApplyRadialCurve(Vector2 v, Func<float, float> magnitudeMap)
    {
        float len = v.Length();
        if (len == 0f)
        {
            return v;
        }
        if (len > 1f)
        {
            return v;
        }

        float mapped = magnitudeMap(len);
        if (mapped <= 0f)
        {
            return Vector2.Zero;
        }
        if (mapped >= 1f)
        {
            return v / len; // normalize to unit magnitude
        }
        return v * (mapped / len);
    }

    /// <summary>
    /// Quadratic response curve (t^2). Lower sensitivity near zero; higher at the end.
    /// </summary>
    public static readonly Func<Vector2, Vector2> CurveQuadratic = v => ApplyRadialCurve(v, t => t * t);

    /// <summary>
    /// Cubic response curve (t^3). Even lower sensitivity near zero; strong at the end.
    /// </summary>
    public static readonly Func<Vector2, Vector2> CurveCubic = v => ApplyRadialCurve(v, t => t * t * t);

    /// <summary>
    /// Square-root response curve (sqrt(t)). Higher sensitivity near zero; softer at the end.
    /// </summary>
    public static readonly Func<Vector2, Vector2> CurveSquareRoot = v => ApplyRadialCurve(v, t => MathF.Sqrt(t));



    /// <summary>
    /// Radial deadzone applied to the aggregated gamepad axis vector. Range: [0, 1].
    /// Values whose length is less than or equal to this threshold are treated as zero.
    /// </summary>
    public float Deadzone { get; set; } = 0.1f;

    /// <summary>
    /// Initializes a new instance of the <see cref="AxisInputAction"/> class.
    /// </summary>
    /// <param name="input">Input system reference.</param>
    /// <param name="gamepad">Optional fixed gamepad; if null, uses <see cref="Input.PrimaryGamepad"/>.</param>
    public AxisInputAction(Input input, Gamepad? gamepad = null)
    {
        _input = input;
        _gamepad = gamepad;
    }


    /// <summary>
    /// Gets the current axis value. If any gamepad axis contributes, returns linear sum with deadzone applied.
    /// Otherwise, returns normalized sum of pressed keys.
    /// </summary>
    public Vector2 RawValue
    {
        get
        {
            Vector2 value = Vector2.Zero;


            bool hasKey = false;
            foreach (var item in _keys)
            {
                if (_input.IsKeyPressing(item.Key))
                {
                    value += item.Value;
                    hasKey = true;
                }
            }

            if (hasKey)
            {
                if (value == Vector2.Zero)
                {
                    return value;
                }
                else
                {
                    value = Vector2.Normalize(value);
                    return _curve != null ? _curve(value) : value;
                }
            }

            Gamepad? gamepad = _gamepad ?? _input.PrimaryGamepad;

            if (gamepad != null)
            {
                foreach (var item in _gamepadAxes)
                {
                    value += item.Value * gamepad.GetAxis(item.Key);
                }
            }

            return _curve != null ? _curve(value) : value;
        }
    }

    public bool IsInputing(out Vector2 value)
    {
        value = RawValue;
        // if the gamepad is inputing
        if (value.LengthSquared() <= Deadzone * Deadzone)
        {
            // Apply radial deadzone for analog input
            value = Vector2.Zero;
            return false;
        }
        return true;
    }

    /// <summary>
    /// Assigns a keyboard key to contribute a directional vector.
    /// </summary>
    public void SetKey(KeyCode key, Vector2 value)
    {
        _keys[key] = value;
    }

    /// <summary>
    /// Removes a keyboard key mapping.
    /// </summary>
    public void RemoveKey(KeyCode key)
    {
        _keys.Remove(key);
    }

    /// <summary>
    /// Assigns a gamepad axis to contribute a directional vector.
    /// </summary>
    public void SetGamepadAxis(GamepadAxis axis, Vector2 value)
    {
        _gamepadAxes[axis] = value;
    }

    /// <summary>
    /// Removes a gamepad axis mapping.
    /// </summary>
    public void RemoveGamepadAxis(GamepadAxis axis)
    {
        _gamepadAxes.Remove(axis);
    }

    /// <summary>
    /// Clears all keyboard and gamepad mappings.
    /// </summary>
    public void Clear()
    {
        _keys.Clear();
        _gamepadAxes.Clear();
    }

    /// <summary>
    /// Sets the optional curve function used to transform the final axis value.
    /// For gamepad input the curve is applied after deadzone. For keyboard input
    /// the curve is applied after normalization.
    /// </summary>
    /// <param name="curve">A function mapping the input vector to a transformed vector.</param>
    public void SetCurve(Func<Vector2, Vector2>? curve)
    {
        _curve = curve;
    }

    /// <summary>
    /// Clears any previously assigned curve function.
    /// </summary>
    public void ClearCurve()
    {
        _curve = null;
    }

    /// <summary>
    /// Exports current mappings and configuration into an option object.
    /// </summary>
    public AxisInputActionOption ToOption()
    {
        return new AxisInputActionOption
        {
            Keys = new Dictionary<KeyCode, Vector2>(_keys),
            GamepadAxes = new Dictionary<GamepadAxis, Vector2>(_gamepadAxes),
            Deadzone = Deadzone,
        };
    }

    /// <summary>
    /// Loads mappings and configuration from an option object.
    /// </summary>
    public void LoadFromOption(AxisInputActionOption option)
    {
        _keys.Clear();
        _gamepadAxes.Clear();
        foreach (var item in option.Keys)
        {
            _keys[item.Key] = item.Value;
        }
        foreach (var item in option.GamepadAxes)
        {
            _gamepadAxes[item.Key] = item.Value;
        }
        Deadzone = option.Deadzone;
    }


}