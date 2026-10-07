using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Alco.Graphics.AlcoGpu.Interop;

/// <summary>
/// Marshals native failures into managed exceptions. Fallible native entries
/// return a <see cref="AlcoGPU.Status"/> code; a failed call has recorded a
/// fresh diagnostic in its thread-local last error, which this helper reads
/// and throws as a <see cref="GraphicsException"/> once the call has
/// returned — throwing from inside a native callback would unwind through
/// native frames, which aborts the process on Unix runtimes.
/// </summary>
internal static unsafe class AlcoGpuMarshal
{
    /// <summary>
    /// Returns <paramref name="status"/> for control-flow values (OK and
    /// NOT_READY) and throws any other status as a
    /// <see cref="GraphicsException"/> carrying the message the failed call
    /// recorded in the thread-local last error.
    /// </summary>
    /// <param name="status">Status returned by the native entry point.</param>
    /// <returns>The unchanged status code.</returns>
    internal static uint ThrowIfFailure(uint status)
    {
        if (status == AlcoGPU.Status.Ok || status == AlcoGPU.Status.NotReady)
        {
            return status;
        }

        AlcoGPU.ErrorInfo info = default;
        AlcoGpuRaw.GetLastError(ref info);
        string message = info.Message != null ? BorrowedString(info.Message) ?? "<no native message>" : "<no native message>";
        throw new GraphicsException($"[alco-gpu:{StatusKind(status)}] {message}");
    }

    /// <summary>
    /// Native log callback: invoked synchronously by alco-gpu from inside
    /// wgpu-core for every record at or below the configured level. It must
    /// never throw — records fire through native frames that do not permit
    /// unwinding — so failures of the managed sink are swallowed here.
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
