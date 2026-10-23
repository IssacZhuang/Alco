// Copyright (c) Amer Koleci and Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repository root for more information.

namespace SDL3;

unsafe partial class SDL3
{
    /// <summary>Returns the bits per sample of an audio format.</summary>
    /// <param name="x">The audio format.</param>
    /// <returns>The bits per sample.</returns>
    public static ushort SDL_AUDIO_BITSIZE(SDL_AudioFormat x)
    {
        return (ushort)((uint)x & SDL_AUDIO_MASK_BITSIZE);
    }

    /// <summary>Returns the bytes per sample of an audio format.</summary>
    /// <param name="x">The audio format.</param>
    /// <returns>The bytes per sample.</returns>
    public static ushort SDL_AUDIO_BYTESIZE(SDL_AudioFormat x) => (ushort)(SDL_AUDIO_BITSIZE(x) / 8);

    /// <summary>Checks whether an audio format stores floating-point samples.</summary>
    /// <param name="x">The audio format.</param>
    /// <returns>True when samples are floating point.</returns>
    public static bool SDL_AUDIO_ISFLOAT(SDL_AudioFormat x)
    {
        return ((uint)x & SDL_AUDIO_MASK_FLOAT) != 0;
    }

    /// <summary>Checks whether an audio format is big-endian.</summary>
    /// <param name="x">The audio format.</param>
    /// <returns>True when the format is big-endian.</returns>
    public static bool SDL_AUDIO_ISBIGENDIAN(SDL_AudioFormat x)
    {
        return ((uint)x & SDL_AUDIO_MASK_BIG_ENDIAN) != 0;
    }

    /// <summary>Checks whether an audio format uses signed samples.</summary>
    /// <param name="x">The audio format.</param>
    /// <returns>True when samples are signed.</returns>
    public static bool SDL_AUDIO_ISSIGNED(SDL_AudioFormat x)
    {
        return ((uint)x & SDL_AUDIO_MASK_SIGNED) != 0;
    }

    /// <summary>Checks whether an audio format stores integer samples.</summary>
    /// <param name="x">The audio format.</param>
    /// <returns>True when samples are integers.</returns>
    public static bool SDL_AUDIO_ISINT(SDL_AudioFormat x) => !SDL_AUDIO_ISFLOAT(x);
    /// <summary>Checks whether an audio format is little-endian.</summary>
    /// <param name="x">The audio format.</param>
    /// <returns>True when the format is little-endian.</returns>
    public static bool SDL_AUDIO_ISLITTLEENDIAN(SDL_AudioFormat x) => !SDL_AUDIO_ISBIGENDIAN(x);
    /// <summary>Checks whether an audio format uses unsigned samples.</summary>
    /// <param name="x">The audio format.</param>
    /// <returns>True when samples are unsigned.</returns>
    public static bool SDL_AUDIO_ISUNSIGNED(SDL_AudioFormat x) => !SDL_AUDIO_ISSIGNED(x);

    /// <summary>Returns the size in bytes of one audio frame.</summary>
    /// <param name="spec">The audio specification.</param>
    /// <returns>The frame size in bytes.</returns>
    public static int SDL_AUDIO_FRAMESIZE(in SDL_AudioSpec spec) => SDL_AUDIO_BYTESIZE(spec.format) * spec.channels;

    /// <summary>Returns the identifiers of all audio playback devices.</summary>
    /// <returns>A span over the playback device identifiers.</returns>
    public static ReadOnlySpan<SDL_AudioDeviceID> SDL_GetAudioPlaybackDevices()
    {
        SDL_AudioDeviceID* ptr = SDL_GetAudioPlaybackDevices(out int count);
        return new(ptr, count);
    }

    /// <summary>Returns the identifiers of all audio recording devices.</summary>
    /// <returns>A span over the recording device identifiers.</returns>
    public static ReadOnlySpan<SDL_AudioDeviceID> SDL_GetAudioRecordingDevices()
    {
        SDL_AudioDeviceID* ptr = SDL_GetAudioRecordingDevices(out int count);
        return new(ptr, count);
    }
}
