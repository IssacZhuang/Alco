using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Alco.Rendering;

namespace Alco.Engine;

/// <summary>
/// Default implementation of <see cref="IInputPromptIconProvider"/> that maintains
/// in-memory mappings from input enums to <see cref="Texture2D"/> icons.
/// </summary>
public sealed class DefaultInputPromptIconProvider : IInputPromptIconProvider
{
    private readonly Dictionary<KeyCode, Texture2D> _keyToIcon = new();
    private readonly Dictionary<Mouse, Texture2D> _mouseToIcon = new();
    // Default mappings when no specific GamepadType mapping is available
    private readonly Dictionary<GamepadButton, Texture2D> _gamepadButtonToIcon = new();
    private readonly Dictionary<GamepadAxis, Texture2D> _gamepadAxisToIcon = new();

    // Per-type mappings
    private readonly Dictionary<GamepadType, Dictionary<GamepadButton, Texture2D>> _typeToGamepadButtonToIcon = new();
    private readonly Dictionary<GamepadType, Dictionary<GamepadAxis, Texture2D>> _typeToGamepadAxisToIcon = new();

    /// <summary>
    /// Associates a keyboard <see cref="KeyCode"/> with an icon texture.
    /// </summary>
    /// <param name="key">The key code to associate.</param>
    /// <param name="icon">The texture used as the icon.</param>
    public void SetIcon(KeyCode key, Texture2D icon)
    {
        _keyToIcon[key] = icon;
    }

    /// <summary>
    /// Associates a mouse button with an icon texture.
    /// </summary>
    /// <param name="mouse">The mouse button to associate.</param>
    /// <param name="icon">The texture used as the icon.</param>
    public void SetIcon(Mouse mouse, Texture2D icon)
    {
        _mouseToIcon[mouse] = icon;
    }

    /// <summary>
    /// Associates a gamepad <see cref="GamepadButton"/> with an icon texture.
    /// </summary>
    /// <param name="button">The gamepad button to associate.</param>
    /// <param name="icon">The texture used as the icon.</param>
    public void SetIcon(GamepadButton button, Texture2D icon)
    {
        _gamepadButtonToIcon[button] = icon;
    }

    /// <summary>
    /// Associates a gamepad <see cref="GamepadButton"/> with an icon texture for a specific <see cref="GamepadType"/>.
    /// </summary>
    /// <param name="gamepadType">The gamepad type to associate with.</param>
    /// <param name="button">The gamepad button to associate.</param>
    /// <param name="icon">The texture used as the icon.</param>
    public void SetIcon(GamepadType gamepadType, GamepadButton button, Texture2D icon)
    {
        if (!_typeToGamepadButtonToIcon.TryGetValue(gamepadType, out var map))
        {
            map = new Dictionary<GamepadButton, Texture2D>();
            _typeToGamepadButtonToIcon[gamepadType] = map;
        }
        map[button] = icon;
    }

    /// <summary>
    /// Associates a gamepad <see cref="GamepadAxis"/> with an icon texture.
    /// </summary>
    /// <param name="axis">The gamepad axis to associate.</param>
    /// <param name="icon">The texture used as the icon.</param>
    public void SetIcon(GamepadAxis axis, Texture2D icon)
    {
        _gamepadAxisToIcon[axis] = icon;
    }

    /// <summary>
    /// Associates a gamepad <see cref="GamepadAxis"/> with an icon texture for a specific <see cref="GamepadType"/>.
    /// </summary>
    /// <param name="gamepadType">The gamepad type to associate with.</param>
    /// <param name="axis">The gamepad axis to associate.</param>
    /// <param name="icon">The texture used as the icon.</param>
    public void SetIcon(GamepadType gamepadType, GamepadAxis axis, Texture2D icon)
    {
        if (!_typeToGamepadAxisToIcon.TryGetValue(gamepadType, out var map))
        {
            map = new Dictionary<GamepadAxis, Texture2D>();
            _typeToGamepadAxisToIcon[gamepadType] = map;
        }
        map[axis] = icon;
    }

    /// <inheritdoc />
    public bool TryGetIcon(KeyCode key, [NotNullWhen(true)] out Texture2D? icon)
    {
        return _keyToIcon.TryGetValue(key, out icon);
    }

    /// <inheritdoc />
    public bool TryGetIcon(Mouse mouse, [NotNullWhen(true)] out Texture2D? icon)
    {
        return _mouseToIcon.TryGetValue(mouse, out icon);
    }

    /// <inheritdoc />
    public bool TryGetIcon(GamepadType gamepadType, GamepadButton button, [NotNullWhen(true)] out Texture2D? icon)
    {
        // Prefer specific type mapping
        if (_typeToGamepadButtonToIcon.TryGetValue(gamepadType, out var map) && map.TryGetValue(button, out icon))
        {
            return true;
        }
        // Fallback to default mapping
        return _gamepadButtonToIcon.TryGetValue(button, out icon);
    }

    /// <inheritdoc />
    public bool TryGetIcon(GamepadType gamepadType, GamepadAxis axis, [NotNullWhen(true)] out Texture2D? icon)
    {
        // Prefer specific type mapping
        if (_typeToGamepadAxisToIcon.TryGetValue(gamepadType, out var map) && map.TryGetValue(axis, out icon))
        {
            return true;
        }
        // Fallback to default mapping
        return _gamepadAxisToIcon.TryGetValue(axis, out icon);
    }
}


