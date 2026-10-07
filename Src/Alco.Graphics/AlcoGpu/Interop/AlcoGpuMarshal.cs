using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Alco.Graphics.AlcoGpu.Interop;

/// <summary>
/// Marshals native failures into managed exceptions. The process-wide error
/// callback registered by <see cref="AlcoGpuNative"/> records each failure,
/// and the facade rethrows it as a <see cref="GraphicsException"/> once the
/// native call has returned: throwing from inside the callback would have to
/// unwind through native frames, which aborts the process on Unix runtimes.
/// </summary>
internal static unsafe class AlcoGpuMarshal
{
    /// <summary>Failure recorded by the error callback for the native call
    /// currently in flight on this thread; consumed by the facade.</summary>
    [ThreadStatic]
    private static (uint Status, string Message)? _pendingError;

    /// <summary>
    /// Native error callback: invoked synchronously by alco-gpu on the calling
    /// thread when an entry point fails. Records the failure for the
    /// <see cref="AlcoGpuNative"/> facade to throw after the call returns —
    /// it must never throw through native frames.
    /// </summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    internal static void OnNativeError(uint status, byte* message, void* userdata)
    {
        string text = BorrowedString(message) ?? "<no native message>";
        _pendingError = (status, text);
    }

    /// <summary>
    /// Returns <paramref name="status"/> for control-flow values and throws the
    /// failure recorded during the call (falling back to the thread-local last
    /// error) as a <see cref="GraphicsException"/>. Mirrors the native guard,
    /// which fires the callback for every status except OK and NOT_READY.
    /// </summary>
    /// <param name="status">Status returned by the native entry point.</param>
    /// <returns>The unchanged status code.</returns>
    internal static uint ThrowIfFailure(uint status)
    {
        if (status == AlcoGPU.Status.Ok || status == AlcoGPU.Status.NotReady)
        {
            return status;
        }

        (uint recordedStatus, string message)? pending = _pendingError;
        _pendingError = null;
        if (pending is not null)
        {
            throw new GraphicsException($"[alco-gpu:{StatusKind(pending.Value.recordedStatus)}] {pending.Value.message}");
        }

        // Defensive fallback: a failure without a recorded callback message
        // still carries the thread-local last error in the native library.
        AlcoGPU.ErrorInfo info = default;
        AlcoGpuRaw.GetLastError(ref info);
        string text = info.Message != null ? BorrowedString(info.Message) ?? "<no native message>" : "<no native message>";
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
