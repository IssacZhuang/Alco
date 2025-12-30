
using System.Diagnostics.CodeAnalysis;
using Alco.Rendering;

namespace Alco.Engine;

public interface IInputPromptIconProvider
{
    bool TryGetIcon(KeyCode key, [NotNullWhen(true)] out Sprite? icon);
    bool TryGetIcon(Mouse mouse, [NotNullWhen(true)] out Sprite? icon);
    bool TryGetIcon(GamepadType gamepadType, GamepadButton button, [NotNullWhen(true)] out Sprite? icon);
    bool TryGetIcon(GamepadType gamepadType, GamepadAxis axis, [NotNullWhen(true)] out Sprite? icon);
}