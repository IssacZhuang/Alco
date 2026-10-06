using System.Diagnostics;
using Alco.Graphics.AlcoGpu.Interop;

namespace Alco.Graphics.AlcoGpu;

/// <summary>
/// Routes wgpu-core log records into the active managed device. The native
/// forwarder is process-wide (a process has one <c>log</c> logger), so the
/// most recently attached device receives every record; records emitted
/// before the first attach or after the last detach are dropped. Routing
/// never throws — records arrive on native frames that do not permit
/// unwinding.
/// </summary>
internal static unsafe class AlcoGpuLogRouter
{
    private static int _forwardingEnabled;
    private static volatile AlcoGpuDevice? _attached;

    /// <summary>
    /// Attaches the device as the log sink and enables native forwarding once
    /// per process. Forwarding is best-effort: when another library owns the
    /// native logger, registration reports the conflict and the engine keeps
    /// running without wgpu-core diagnostics rather than failing the device.
    /// </summary>
    /// <param name="device">The device that receives subsequent records.</param>
    public static void Attach(AlcoGpuDevice device)
    {
        try
        {
            _attached = device;
            if (Interlocked.Exchange(ref _forwardingEnabled, 1) == 1)
            {
                return;
            }

            try
            {
                AlcoGpuNative.SetLogCallback(&AlcoGpuMarshal.OnNativeLog, null);
            }
            catch (GraphicsException e)
            {
                Debug.WriteLine($"[alco-gpu] native log forwarding unavailable: {e.Message}");
            }
        }
        finally
        {
            GC.KeepAlive(device);
        }
    }

    /// <summary>
    /// Detaches the device when it is disposed so late native records are
    /// dropped instead of reaching a disposed device's host.
    /// </summary>
    /// <param name="device">The device being disposed.</param>
    public static void Detach(AlcoGpuDevice device)
    {
        if (ReferenceEquals(_attached, device))
        {
            _attached = null;
        }
    }

    /// <summary>Routes one native record; must never throw (native frames).</summary>
    /// <param name="level">The <see cref="AlcoGPU.LogLevel"/> of the record.</param>
    /// <param name="message">The record text.</param>
    internal static void Route(uint level, string message)
    {
        _attached?.RouteNativeLog(level, message);
    }
}
