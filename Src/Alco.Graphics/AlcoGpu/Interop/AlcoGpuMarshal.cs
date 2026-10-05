using System.Text;

namespace Alco.Graphics.AlcoGpu.Interop;

/// <summary>
/// Marshals native status codes and borrowed strings into managed exceptions
/// and text. Failures carry the native message from the thread-local
/// last-error slot.
/// </summary>
internal static unsafe class AlcoGpuMarshal
{
    /// <summary>
    /// Throws a <see cref="GraphicsException"/> carrying the native message
    /// when <paramref name="status"/> is not OK. <see cref="AlcoGpuAbi.Status.NotReady"/>
    /// is a control-flow value and must be handled by callers before this check.
    /// </summary>
    public static void ThrowIfFailed(uint status, [System.Runtime.CompilerServices.CallerArgumentExpression(nameof(status))] string? call = null)
    {
        if (status == AlcoGpuAbi.Status.Ok || status == AlcoGpuAbi.Status.NotReady)
        {
            return;
        }

        AlcoErrorInfo info = default;
        AlcoGpuNative.GetLastError(ref info);
        string message = BorrowedString(info.Message) ?? "<no native message>";
        string kind = status switch
        {
            AlcoGpuAbi.Status.InvalidHandle => "invalid handle",
            AlcoGpuAbi.Status.InvalidArgument => "invalid argument",
            AlcoGpuAbi.Status.Validation => "validation",
            AlcoGpuAbi.Status.OutOfMemory => "out of memory",
            AlcoGpuAbi.Status.DeviceLost => "device lost",
            AlcoGpuAbi.Status.Panic => "native panic",
            AlcoGpuAbi.Status.Unsupported => "unsupported",
            _ => "failure",
        };
        throw new GraphicsException($"[alco-gpu:{kind}] {call}: {message}");
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
