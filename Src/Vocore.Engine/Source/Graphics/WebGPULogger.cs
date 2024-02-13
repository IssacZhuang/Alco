using System.Runtime.CompilerServices;
using Vocore.Graphics;

namespace Vocore.Engine
{
    internal static class GraphicsLogger
    {
        internal static void RegisterLogger()
        {
            Graphics.GraphicsLogger.ErrorCallback = LogError;
            Graphics.GraphicsLogger.WarningCallback = LogWarning;
            Graphics.GraphicsLogger.InfoCallback = LogInfo;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void LogError(string message)
        {
            Log.Error("[WGPU Error]" + message);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void LogWarning(string message)
        {
            Log.Warning("[WGPU Warning]" + message);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void LogInfo(string message)
        {
            Log.Info("[WGPU Info]" + message);
        }
    }
}