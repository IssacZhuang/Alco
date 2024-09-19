using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SteamAudio;

using static SteamAudio.IPL;

namespace Vocore.Audio.Steam;

internal unsafe class SteamAudioDevice : AudioDevice
{
    private readonly Context _context;

    public Context Context
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _context;
    }

    public SteamAudioDevice()
    {
        ContextSettings settings = new ContextSettings()
        {
            Version = IPL.Version,
        };

        

        Error error = ContextCreate(settings, out _context);
        if (error != Error.Success)
        {
            throw new AudioException("Failed to create steam audio device");
        }
    }

    protected override AudioBuffer CreateBufferCore()
    {
        return new SteamAudioBuffer(this);
    }

    protected override void Dispose(bool disposing)
    {

    }

    private static void LogCallback(LogLevel level, string message)
    {

    }
}