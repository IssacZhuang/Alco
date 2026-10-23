// Copyright (c) Amer Koleci and Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repository root for more information.

using static SDL3.SDL3;

namespace SDL3;

/// <summary>Types of haptic effects supported by SDL.</summary>
public enum SDL_HapticEffectType : ushort
{
    Constant = SDL_HAPTIC_CONSTANT,
    Sine = SDL_HAPTIC_SINE,
    LeftRight = SDL_HAPTIC_LEFTRIGHT,
    Triangle = SDL_HAPTIC_TRIANGLE,
    SawToothUp = SDL_HAPTIC_SAWTOOTHUP,
    SawToothDown = SDL_HAPTIC_SAWTOOTHDOWN,
    Spring = SDL_HAPTIC_SPRING,
    Damper = SDL_HAPTIC_DAMPER,
    Inertia = SDL_HAPTIC_INERTIA,
    Friction = SDL_HAPTIC_FRICTION,
    Custom = SDL_HAPTIC_CUSTOM,
    Gain = SDL_HAPTIC_GAIN,
    AutoCenter = SDL_HAPTIC_AUTOCENTER,
    Status = SDL_HAPTIC_STATUS,
    Pause = SDL_HAPTIC_PAUSE,
}

/// <summary>Coordinate systems used to specify a haptic direction.</summary>
public enum SDL_HapticDirectionType : byte
{
    Polar = SDL_HAPTIC_POLAR,
    Cartesian = SDL_HAPTIC_CARTESIAN,
    Spherical = SDL_HAPTIC_SPHERICAL,
    SteeringAxis = SDL_HAPTIC_STEERING_AXIS,
}

unsafe partial class SDL3
{
    /* SDL_HapticEffect type */
    /// <summary>The device supports constant force effects.</summary>
    public const ushort SDL_HAPTIC_CONSTANT = (1 << 0);
    /// <summary>The device supports sine wave effects.</summary>
    public const ushort SDL_HAPTIC_SINE = (1 << 1);
    /// <summary>The device supports left/right effects.</summary>
    public const ushort SDL_HAPTIC_LEFTRIGHT = (1 << 2);
    /// <summary>The device supports triangle wave effects.</summary>
    public const ushort SDL_HAPTIC_TRIANGLE = (1 << 3);
    /// <summary>The device supports sawtooth-up effects.</summary>
    public const ushort SDL_HAPTIC_SAWTOOTHUP = (1 << 4);
    /// <summary>The device supports sawtooth-down effects.</summary>
    public const ushort SDL_HAPTIC_SAWTOOTHDOWN = (1 << 5);
    /// <summary>The device supports spring effects.</summary>
    public const ushort SDL_HAPTIC_SPRING = (1 << 7);
    /// <summary>The device supports damper effects.</summary>
    public const ushort SDL_HAPTIC_DAMPER = (1 << 8);
    /// <summary>The device supports inertia effects.</summary>
    public const ushort SDL_HAPTIC_INERTIA = (1 << 9);
    /// <summary>The device supports friction effects.</summary>
    public const ushort SDL_HAPTIC_FRICTION = (1 << 10);
    /// <summary>The device supports custom effects.</summary>
    public const ushort SDL_HAPTIC_CUSTOM = (1 << 11);
    /// <summary>The device supports gain control.</summary>
    public const ushort SDL_HAPTIC_GAIN = (1 << 12);
    /// <summary>The device supports autocenter control.</summary>
    public const ushort SDL_HAPTIC_AUTOCENTER = (1 << 13);
    /// <summary>The device supports status queries.</summary>
    public const ushort SDL_HAPTIC_STATUS = (1 << 14);
    /// <summary>The device supports pausing effects.</summary>
    public const ushort SDL_HAPTIC_PAUSE = (1 << 15);

    /* SDL_HapticDirection type */
    /// <summary>Polar direction coordinates.</summary>
    public const byte SDL_HAPTIC_POLAR = 0;
    /// <summary>Cartesian direction coordinates.</summary>
    public const byte SDL_HAPTIC_CARTESIAN = 1;
    /// <summary>Spherical direction coordinates.</summary>
    public const byte SDL_HAPTIC_SPHERICAL = 2;
    /// <summary>Steering-axis direction coordinates.</summary>
    public const byte SDL_HAPTIC_STEERING_AXIS = 3;

    /* SDL_HapticRunEffect */
    /// <summary>Duration value that repeats a haptic effect indefinitely.</summary>
    public const uint SDL_HAPTIC_INFINITY = 4294967295U;

    /// <summary>Returns the identifiers of all connected haptic devices.</summary>
    /// <returns>A span over the haptic device identifiers.</returns>
    public static ReadOnlySpan<SDL_HapticID> SDL_GetHaptics()
    {
        SDL_HapticID* ptr = SDL_GetHaptics(out int count);
        return new(ptr, count);
    }
}
