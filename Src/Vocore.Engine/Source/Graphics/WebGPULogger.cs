using System.Runtime.CompilerServices;
using Vocore.Graphics;
using Vocore.Graphics.WebGPU;

namespace Vocore.Engine
{
    internal static class WebGPULogger
    {
        internal static void RegisterLogger()
        {
            GraphicsLogger.ErrorCallback = LogError;
            GraphicsLogger.WarningCallback = LogWarning;
            GraphicsLogger.InfoCallback = LogInfo;
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