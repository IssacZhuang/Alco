using System.Runtime.CompilerServices;

namespace Vocore.Graphics;

public static class GraphicsLogger
{
    public static Action<string>? ErrorCallback { get; set; }
    public static Action<string>? WarningCallback { get; set; }
    public static Action<string>? InfoCallback { get; set; }

    private static bool _muteWarning;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Info(string message)
    {
        InfoCallback?.Invoke(message);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Warning(string message)
    {
        if (_muteWarning)
        {
            return;
        }
        WarningCallback?.Invoke(message);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Error(string message)
    {
        ErrorCallback?.Invoke(message);
    }

    public static void MuteWarning(bool mute)
    {
        _muteWarning = mute;
    }

    public static void ResetWarning()
    {
        _muteWarning = false;
    }
}