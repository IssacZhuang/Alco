namespace Alco.Engine;

#if DIAGHUB_ENABLE_TRACE_SYSTEM
using Microsoft.DiagnosticsHub;
#endif

/// <summary>
/// Emits a named trace range to the Visual Studio diagnostics hub when tracing is enabled.
/// </summary>
public class VisualStudioProfiler: IDisposable
{
#if DIAGHUB_ENABLE_TRACE_SYSTEM
    private readonly UserMarkRange _userMarkRange;
#endif

    /// <summary>
    /// Initializes a new instance of <see cref="VisualStudioProfiler"/> and starts a named range.
    /// </summary>
    /// <param name="name">The range name shown in the profiler.</param>
    public VisualStudioProfiler(string name)
    {
#if DIAGHUB_ENABLE_TRACE_SYSTEM
        _userMarkRange = new UserMarkRange(name);
#endif
    }

    /// <summary>
    /// Ends the trace range.
    /// </summary>
    public void Dispose()
    {
#if DIAGHUB_ENABLE_TRACE_SYSTEM
        _userMarkRange.Dispose();
#endif
    }
}
