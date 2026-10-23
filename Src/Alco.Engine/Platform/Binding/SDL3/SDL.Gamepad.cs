// Copyright (c) Amer Koleci and Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repository root for more information.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace SDL3;

public partial struct SDL_GamepadBinding
{
    /// <summary>Gets a reference to the input button binding.</summary>
    [UnscopedRef]
    public ref int input_button
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            return ref input.button;
        }
    }

    /// <summary>Gets a reference to the input axis binding.</summary>
    [UnscopedRef]
    public ref SDL_GamepadBinding_input.SDL_GamepadBinding_axis input_axis
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            return ref input.axis;
        }
    }

    /// <summary>Gets a reference to the input hat binding.</summary>
    [UnscopedRef]
    public ref SDL_GamepadBinding_input.SDL_GamepadBinding_hat input_hat
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            return ref input.hat;
        }
    }

    /// <summary>Gets a reference to the output button binding.</summary>
    [UnscopedRef]
    public ref SDL_GamepadButton output_button
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            return ref output.button;
        }
    }

    /// <summary>Gets a reference to the output axis binding.</summary>
    [UnscopedRef]
    public ref SDL_GamepadBinding_output.SDL_GamepadBinding_axis output_axis
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            return ref output.axis;
        }
    }
}

unsafe partial class SDL3
{
    /// <summary>Returns the identifiers of all connected gamepads.</summary>
    /// <returns>A span over the gamepad identifiers.</returns>
    public static ReadOnlySpan<SDL_JoystickID> SDL_GetGamepads()
    {
        SDL_JoystickID* ptr = SDL_GetGamepads(out int count);
        return new(ptr, count);
    }

    //public static ReadOnlySpan<SDL_GamepadBinding> SDL_GetGamepadBindings(SDL_Gamepad gamepad)
    //{
    //    SDL_GamepadBinding* ptr = SDL_GetGamepadBindings(gamepad, out int count);
    //    return new(ptr, count);
    //}

    /// <summary>Returns the available gamepad mapping strings.</summary>
    /// <returns>The gamepad mapping strings.</returns>
    public static string[] SDL_GetGamepadMappings()
    {
        byte** strings = SDL_GetGamepadMappings(out int count);
        string[] names = new string[count];
        for (int i = 0; i < count; i++)
        {
            names[i] = ConvertToManaged(strings[i])!;
        }

        return names;
    }

    /// <summary>Sends an effect to a gamepad from an array.</summary>
    /// <typeparam name="T">The unmanaged effect data type.</typeparam>
    /// <param name="gamepad">The gamepad to send the effect to.</param>
    /// <param name="source">The effect data.</param>
    /// <returns>True on success; otherwise false.</returns>
    public static SDLBool SDL_SendGamepadEffect<T>(SDL_Gamepad gamepad, T[] source) where T : unmanaged
    {
        ReadOnlySpan<T> span = source.AsSpan();

        return SDL_SendGamepadEffect(gamepad, span);
    }

    /// <summary>Sends an effect to a gamepad from a span.</summary>
    /// <typeparam name="T">The unmanaged effect data type.</typeparam>
    /// <param name="gamepad">The gamepad to send the effect to.</param>
    /// <param name="data">The effect data.</param>
    /// <returns>True on success; otherwise false.</returns>
    public static SDLBool SDL_SendGamepadEffect<T>(SDL_Gamepad gamepad, ReadOnlySpan<T> data) where T : unmanaged
    {
        return SDL_SendGamepadEffect(gamepad, ref MemoryMarshal.GetReference(data), data.Length * sizeof(T));
    }

    /// <summary>Sends an effect to a gamepad from a reference.</summary>
    /// <typeparam name="T">The unmanaged effect data type.</typeparam>
    /// <param name="gamepad">The gamepad to send the effect to.</param>
    /// <param name="data">The effect data.</param>
    /// <param name="size">The effect data size in bytes.</param>
    /// <returns>True on success; otherwise false.</returns>
    public static SDLBool SDL_SendGamepadEffect<T>(SDL_Gamepad gamepad, ref T data, int size) where T : unmanaged
    {
        fixed (void* dataPtr = &data)
        {
            return SDL_SendGamepadEffect(gamepad, (nint)dataPtr, size);
        }
    }

    /// <summary>Checks whether a joystick is an Amazon Luna controller.</summary>
    /// <param name="vendor_id">The USB vendor identifier.</param>
    /// <param name="product_id">The USB product identifier.</param>
    /// <returns>True when the identifiers match an Amazon Luna controller.</returns>
    public static bool SDL_IsJoystickAmazonLunaController(ushort vendor_id, ushort product_id)
    {
        return ((vendor_id == 0x1949 && product_id == 0x0419) ||
                (vendor_id == 0x0171 && product_id == 0x0419));
    }

    /// <summary>Checks whether a joystick is a Google Stadia controller.</summary>
    /// <param name="vendor_id">The USB vendor identifier.</param>
    /// <param name="product_id">The USB product identifier.</param>
    /// <returns>True when the identifiers match a Google Stadia controller.</returns>
    public static bool SDL_IsJoystickGoogleStadiaController(ushort vendor_id, ushort product_id)
    {
        return (vendor_id == 0x18d1 && product_id == 0x9400);
    }

    /// <summary>Checks whether a joystick is an NVIDIA Shield controller.</summary>
    /// <param name="vendor_id">The USB vendor identifier.</param>
    /// <param name="product_id">The USB product identifier.</param>
    /// <returns>True when the identifiers match an NVIDIA Shield controller.</returns>
    public static bool SDL_IsJoystickNVIDIASHIELDController(ushort vendor_id, ushort product_id)
    {
        return (vendor_id == 0x0955 && (product_id == 0x7210 || product_id == 0x7214));
    }
}
