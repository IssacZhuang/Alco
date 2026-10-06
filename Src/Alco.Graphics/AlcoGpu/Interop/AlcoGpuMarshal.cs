using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Alco.Graphics.AlcoGpu.Interop;

/// <summary>
/// Marshals native failures into managed exceptions. The process-wide error
/// callback registered by <see cref="AlcoGpuNative"/> throws directly from
/// the native call site, so every alco-gpu failure becomes a
/// <see cref="GraphicsException"/> without per-call status checks.
/// </summary>
internal static unsafe class AlcoGpuMarshal
{
    /// <summary>
    /// Native error callback (C-unwind): invoked synchronously by alco-gpu on
    /// the calling thread when an entry point fails. Thrown exceptions unwind
    /// through the native frames back into the managed caller, mirroring the
    /// former wgpu-native uncaptured-error handling.
    /// </summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    internal static void OnNativeError(uint status, byte* message, void* userdata)
    {
        string text = BorrowedString(message) ?? "<no native message>";
        throw new GraphicsException($"[alco-gpu:{StatusKind(status)}] {text}");
    }

    /// <summary>
    /// Native log callback: invoked synchronously by alco-gpu from inside
    /// wgpu-core for every record at or below the configured level. Unlike
    /// <see cref="OnNativeError"/> it must never throw — records fire through
    /// native frames that do not permit unwinding — so failures of the
    /// managed sink are swallowed here.
    /// </summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    internal static void OnNativeLog(uint level, byte* message, void* userdata)
    {
        string? text = BorrowedString(message);
        if (text is null)
        {
            return;
        }

        try
        {
            AlcoGpuLogRouter.Route(level, text);
        }
        catch
        {
            // Never unwind through native wgpu-core frames.
        }
    }

    /// <summary>Maps an <see cref="AlcoGPU.Status"/> value to its short name.</summary>
    internal static string StatusKind(uint status)
    {
        return status switch
        {
            AlcoGPU.Status.InvalidHandle => "invalid handle",
            AlcoGPU.Status.InvalidArgument => "invalid argument",
            AlcoGPU.Status.Validation => "validation",
            AlcoGPU.Status.OutOfMemory => "out of memory",
            AlcoGPU.Status.DeviceLost => "device lost",
            AlcoGPU.Status.Panic => "native panic",
            AlcoGPU.Status.Unsupported => "unsupported",
            _ => "failure",
        };
    }

    /// <summary>Reads a borrowed NUL-terminated UTF-8 string; null for null pointers.</summary>
    public static string? BorrowedString(byte* pointer)
    {
        return pointer == null ? null : Encoding.UTF8.GetString(pointer, StringLength(pointer));
    }

    /// <summary>Computes the byte length of a NUL-terminated UTF-8 string.</summary>
    public static int StringLength(byte* pointer)
    {
        int length = 0;
        while (pointer[length] != 0)
        {
            length++;
        }
        return length;
    }
}
