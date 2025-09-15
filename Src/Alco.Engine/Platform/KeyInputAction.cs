
namespace Alco.Engine;

public sealed class KeyInputAction
{
    private readonly Input _input;
    private readonly Gamepad? _gamepad;

    private readonly HashSet<KeyCode> _keys = new();
    private readonly HashSet<GamepadButton> _gamepadButtons = new();
    private readonly HashSet<Mouse> _mouseButtons = new();

    public IReadOnlyCollection<KeyCode> Keys => _keys;
    public IReadOnlyCollection<GamepadButton> GamepadButtons => _gamepadButtons;
    public IReadOnlyCollection<Mouse> MouseButtons => _mouseButtons;

    public KeyInputAction(Input input, Gamepad? gamepad = null)
    {
        _input = input;
        _gamepad = gamepad;
    }

    /// <summary>
    /// Returns true if any configured key, mouse, or gamepad button was pressed this frame.
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

            foreach (var mouse in _mouseButtons)
            {
                if (_input.IsMouseDown(mouse))
                {
                    return true;
                }
            }

            Gamepad? gamepad = _gamepad ?? _input.PrimaryGamepad;
            if (gamepad != null)
            {
                foreach (var button in _gamepadButtons)
                {
                    if (gamepad.IsButtonDown(button))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Returns true if any configured key, mouse, or gamepad button was released this frame.
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

            foreach (var mouse in _mouseButtons)
            {
                if (_input.IsMouseUp(mouse))
                {
                    return true;
                }
            }

            Gamepad? gamepad = _gamepad ?? _input.PrimaryGamepad;
            if (gamepad != null)
            {
                foreach (var button in _gamepadButtons)
                {
                    if (gamepad.IsButtonUp(button))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Returns true if any configured key, mouse, or gamepad button is currently held down.
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

            foreach (var mouse in _mouseButtons)
            {
                if (_input.IsMousePressing(mouse))
                {
                    return true;
                }
            }

            Gamepad? gamepad = _gamepad ?? _input.PrimaryGamepad;
            if (gamepad != null)
            {
                foreach (var button in _gamepadButtons)
                {
                    if (gamepad.IsButtonPressing(button))
                    {
                        return true;
                    }
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

    public void Clear()
    {
        _keys.Clear();
        _gamepadButtons.Clear();
        _mouseButtons.Clear();
    }

    public KeyInputActionOption ToOption()
    {
        return new KeyInputActionOption
        {
            Keys = _keys.ToList(),
            GamepadButtons = _gamepadButtons.ToList(),
            MouseButtons = _mouseButtons.ToList(),
        };
    }

    public void LoadFromOption(KeyInputActionOption option)
    {
        _keys.Clear();
        _gamepadButtons.Clear();
        _mouseButtons.Clear();
        for (int i = 0; i < option.Keys.Count; i++)
        {
            _keys.Add(option.Keys[i]);
        }
        for (int i = 0; i < option.GamepadButtons.Count; i++)
        {
            _gamepadButtons.Add(option.GamepadButtons[i]);
        }
        for (int i = 0; i < option.MouseButtons.Count; i++)
        {
            _mouseButtons.Add(option.MouseButtons[i]);
        }
    }

    /// <summary>
    /// Adds a mouse button to this action.
    /// </summary>
    /// <param name="button">Mouse button to listen for.</param>
    public void AddMouseButton(Mouse button)
    {
        _mouseButtons.Add(button);
    }

    /// <summary>
    /// Removes a mouse button from this action.
    /// </summary>
    /// <param name="button">Mouse button to remove.</param>
    public void RemoveMouseButton(Mouse button)
    {
        _mouseButtons.Remove(button);
    }
}