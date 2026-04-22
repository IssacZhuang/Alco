using System;
using System.Runtime.InteropServices;

namespace Alco.ImGUI
{
    /// <summary>
    /// Thrown when cimgui's IM_ASSERT fails inside a native call.
    /// </summary>
    public sealed class ImGuiNativeException : Exception
    {
        public string AssertExpression { get; }
        public string SourceFile { get; }
        public int LineNumber { get; }

        public ImGuiNativeException(string expr, string file, int line)
            : base($"[cimgui] Assert failed: {expr} at {file}:{line}")
        {
            AssertExpression = expr;
            SourceFile = file;
            LineNumber = line;
        }
    }

    internal static unsafe partial class ImGuiNative
    {
        [DllImport("cimgui", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void cimgui_set_error_callback(delegate* unmanaged[Cdecl]<byte*, byte*, int, void> callback);

        [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
        private static void OnNativeError(byte* expr, byte* file, int line)
        {
            throw new ImGuiNativeException(
                Util.StringFromPtr(expr),
                Util.StringFromPtr(file),
                line);
        }

        /// <summary>
        /// Register the error callback. Must be called once after ImGui.CreateContext().
        /// </summary>
        internal static void InitializeErrorHandling()
        {
            cimgui_set_error_callback(&OnNativeError);
        }
    }
}
