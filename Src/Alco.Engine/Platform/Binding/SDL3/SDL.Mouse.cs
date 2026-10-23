// Copyright (c) Amer Koleci and Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repository root for more information.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace SDL3;

/// <summary>Mouse button identifiers used by SDL.</summary>
public enum SDL_Button : uint
{
    Left = SDL3.SDL_BUTTON_LEFT,
    Middle = SDL3.SDL_BUTTON_MIDDLE,
    Right = SDL3.SDL_BUTTON_RIGHT,
    X1 = SDL3.SDL_BUTTON_X1,
    X2 = SDL3.SDL_BUTTON_X2,
}


/// <summary>Bit flags representing pressed mouse buttons.</summary>
[Flags]
public enum SDL_MouseButtonFlags : uint
{
    Left = SDL3.SDL_BUTTON_LMASK,
    Middle = SDL3.SDL_BUTTON_MMASK,
    Right = SDL3.SDL_BUTTON_RMASK,
    X1 = SDL3.SDL_BUTTON_X1MASK,
    X2 = SDL3.SDL_BUTTON_X2MASK,
}

partial class SDL3
{
    /// <summary>Bit flag for the left mouse button.</summary>
    public const uint SDL_BUTTON_LMASK = (1U << ((1) - 1));
    /// <summary>Bit flag for the middle mouse button.</summary>
    public const uint SDL_BUTTON_MMASK = (1U << ((2) - 1));
    /// <summary>Bit flag for the right mouse button.</summary>
    public const uint SDL_BUTTON_RMASK = (1U << ((3) - 1));
    /// <summary>Bit flag for the first extra mouse button.</summary>
    public const uint SDL_BUTTON_X1MASK = (1U << ((4) - 1));
    /// <summary>Bit flag for the second extra mouse button.</summary>
    public const uint SDL_BUTTON_X2MASK = (1U << ((5) - 1));

    /// <summary>Returns the bit flag for the given mouse button.</summary>
    /// <param name="button">The mouse button.</param>
    /// <returns>The corresponding bit flag.</returns>
    public static SDL_MouseButtonFlags SDL_BUTTON(SDL_Button button) => (SDL_MouseButtonFlags)(1 << ((int)button - 1));

    /// <summary>Returns the identifiers of all connected mice.</summary>
    /// <returns>A span over the mouse identifiers.</returns>
    public static unsafe ReadOnlySpan<SDL_MouseID> SDL_GetMice()
    {
        SDL_MouseID* ptr = SDL_GetMice(out int count);
        return new(ptr, count);
    }
}
