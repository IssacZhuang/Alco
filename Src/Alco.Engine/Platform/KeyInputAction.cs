
namespace Alco.Engine;

public sealed class KeyInputAction
{
    private readonly Input _input;
    private readonly Gamepad? _gamepad;

    private readonly HashSet<KeyCode> _keys = new();
    private readonly HashSet<GamepadButton> _gamepadButtons = new();

    public KeyInputAction(Input input, Gamepad? gamepad = null)
    {
        _input = input;
        _gamepad = gamepad;
    }

    /// <summary>
    /// Returns true if any configured key or gamepad button was pressed this frame.
    /// </summary>
    public bool IsDown
    {
        get
        {
            foreach (var key in _keys)
            {
                if (_input.IsKeyDown(key))
                {
                    return true;
                }
            }

            Gamepad? gamepad = _gamepad ?? _input.PrimaryGamepad;

            if(gamepad == null)
            {
                return false;
            }

            foreach (var button in _gamepadButtons)
            {
                if (gamepad.IsButtonDown(button))
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Returns true if any configured key or gamepad button was released this frame.
    /// </summary>
    public bool IsUp
    {
        get
        {
            foreach (var key in _keys)
            {
                if (_input.IsKeyUp(key))
                {
                    return true;
                }
            }

            Gamepad? gamepad = _gamepad ?? _input.PrimaryGamepad;
            if (gamepad == null)
            {
                return false;
            }

            foreach (var button in _gamepadButtons)
            {
                if (gamepad.IsButtonUp(button))
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Returns true if any configured key or gamepad button is currently held down.
    /// </summary>
    public bool IsPressing
    {
        get
        {
            foreach (var key in _keys)
            {
                if (_input.IsKeyPressing(key))
                {
                    return true;
                }
            }

            Gamepad? gamepad = _gamepad ?? _input.PrimaryGamepad;
            if (gamepad == null)
            {
                return false;
            }

            foreach (var button in _gamepadButtons)
            {
                if (gamepad.IsButtonPressing(button))
                {
                    return true;
                }
            }

            return false;
        }
    }

    public void AddKey(KeyCode key)
    {
        _keys.Add(key);
    }

    public void RemoveKey(KeyCode key)
    {
        _keys.Remove(key);
    }

    public void AddGamepadButton(GamepadButton button)
    {
        _gamepadButtons.Add(button);
    }

    public void RemoveGamepadButton(GamepadButton button)
    {
        _gamepadButtons.Remove(button);
    }

    public KeyInputActionOption ToOption()
    {
        return new KeyInputActionOption
        {
            Keys = _keys.ToList(),
            GamepadButtons = _gamepadButtons.ToList(),
        };
    }

    public void LoadFromOption(KeyInputActionOption option)
    {
        _keys.Clear();
        _gamepadButtons.Clear();
        for (int i = 0; i < option.Keys.Count; i++)
        {
            _keys.Add(option.Keys[i]);
        }
        for (int i = 0; i < option.GamepadButtons.Count; i++)
        {
            _gamepadButtons.Add(option.GamepadButtons[i]);
        }
    }
}