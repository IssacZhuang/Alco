// Copyright (c) Amer Koleci and Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repository root for more information.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace SDL3;

public unsafe partial struct SDL_TextInputEvent
{
    /// <summary>Gets the input text.</summary>
    /// <returns>The input text.</returns>
    public readonly string? GetText() => SDL3.ConvertToManaged(text);
}

public unsafe partial struct SDL_TextEditingEvent
{
    /// <summary>Gets the text being edited.</summary>
    /// <returns>The text being edited.</returns>
    public readonly string? GetText() => SDL3.ConvertToManaged(text);
}

public unsafe partial struct SDL_DropEvent
{
    /// <summary>Gets the source of the dropped data.</summary>
    /// <returns>The source, or null when unavailable.</returns>
    public readonly string? GetSource() => SDL3.ConvertToManaged(source);

    /// <summary>Gets the dropped data as text.</summary>
    /// <returns>The dropped text, or null when unavailable.</returns>
    public readonly string? GetData() => SDL3.ConvertToManaged(data);
}

public partial struct SDL_MouseButtonEvent
{
    /// <summary>Gets the mouse button that generated the event.</summary>
    public readonly SDL_Button Button => (SDL_Button)button;
}

public partial struct SDL_GamepadAxisEvent
{
    /// <summary>Gets the gamepad axis that generated the event.</summary>
    public readonly SDL_GamepadAxis Axis => (SDL_GamepadAxis)axis;
}

public partial struct SDL_GamepadButtonEvent
{
    /// <summary>Gets the gamepad button that generated the event.</summary>
    public readonly SDL_GamepadButton Button => (SDL_GamepadButton)button;
}
