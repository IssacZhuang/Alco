using System.Runtime.CompilerServices;
using Vocore.Graphics;

namespace Vocore.Engine
{
    internal static class GraphicsLogger
    {
        private static string Prefix = "[Graphics]";

        internal static void RegisterLogger(string prefix)
        {
            Prefix = prefix;
            Graphics.GraphicsLogger.ErrorCallback = LogError;
            Graphics.GraphicsLogger.WarningCallback = LogWarning;
            Graphics.GraphicsLogger.InfoCallback = LogInfo;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void LogError(string message)
        {
            Log.Error(Prefix, message);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void LogWarning(string message)
        {
            Log.Warning(Prefix, message);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void LogInfo(string message)
        {
            Log.Info(Prefix, message);
        }
    }
}