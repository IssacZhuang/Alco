namespace Alco.Engine;

#if DIAGHUB_ENABLE_TRACE_SYSTEM
using Microsoft.DiagnosticsHub;
#endif

public class VisualStudioProfiler: IDisposable
{
#if DIAGHUB_ENABLE_TRACE_SYSTEM
    private readonly UserMarkRange _userMarkRange;
#endif

    public VisualStudioProfiler(string name)
    {
#if DIAGHUB_ENABLE_TRACE_SYSTEM
        _userMarkRange = new UserMarkRange(name);
#endif
    }

    public void Dispose()
    {
#if DIAGHUB_ENABLE_TRACE_SYSTEM
        _userMarkRange.Dispose();
#endif
    }
}
