using System.Numerics;

namespace Alco.Engine;

public sealed class AxisInputAction
{
    private readonly Input _input;
    private readonly Gamepad? _gamepad;

    private readonly Dictionary<KeyCode, Vector2> _keys = new();
    private readonly Dictionary<GamepadAxis, Vector2> _gamepadAxes = new();

    public AxisInputAction(Input input, Gamepad? gamepad = null)
    {
        _input = input;
        _gamepad = gamepad;
    }


    public Vector2 Value
    {
        get
        {
            Vector2 value = Vector2.Zero;
            Gamepad? gamepad = _gamepad ?? _input.PrimaryGamepad;
            if (gamepad != null)
            {

                foreach (var item in _gamepadAxes)
                {
                    value += item.Value * gamepad.GetAxis(item.Key);
                }
                //the axis of gamepad is linear so it should not be normalized
                return value;
            }

            foreach (var item in _keys)
            {
                if (_input.IsKeyDown(item.Key))
                {
                    value += item.Value;
                }
            }
            return Vector2.Normalize(value);
        }
    }

    public void SetKey(KeyCode key, Vector2 value)
    {
        _keys[key] = value;
    }

    public void RemoveKey(KeyCode key)
    {
        _keys.Remove(key);
    }

    public void SetGamepadAxis(GamepadAxis axis, Vector2 value)
    {
        _gamepadAxes[axis] = value;
    }

    public void RemoveGamepadAxis(GamepadAxis axis)
    {
        _gamepadAxes.Remove(axis);
    }

    public AxisInputActionOption ToOption()
    {
        return new AxisInputActionOption
        {
            Keys = new Dictionary<KeyCode, Vector2>(_keys),
            GamepadAxes = new Dictionary<GamepadAxis, Vector2>(_gamepadAxes),
        };
    }

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
    }


}