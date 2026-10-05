using System.Runtime.InteropServices;

namespace Alco.Graphics.AlcoGpu.Interop;

/// <summary>
/// Loads the alco-gpu shared library by probing the application directory with
/// platform-mapped file names before falling back to the default probe (the
/// bare-name probe fails on Linux/macOS where dlopen does not search the
/// application directory).
/// </summary>
internal static class AlcoGpuNativeLibrary
{
    private static int _loaded;

    /// <summary>Attempts to preload the library so exports resolve deterministically.</summary>
    public static void EnsureLoaded()
    {
        if (Interlocked.Exchange(ref _loaded, 1) == 1)
        {
            return;
        }

        foreach (string fileName in CandidateFileNames())
        {
            string path = Path.Combine(AppContext.BaseDirectory, fileName);
            if (NativeLibrary.TryLoad(path, out nint _))
            {
                return;
            }
        }

        // Let the default P/Invoke probe report a load error on demand.
        NativeLibrary.TryLoad("alco_gpu", out nint _);
    }

    private static IEnumerable<string> CandidateFileNames()
    {
        if (OperatingSystem.IsWindows())
        {
            yield return "alco_gpu.dll";
        }
        else if (OperatingSystem.IsMacOS())
        {
            yield return "libalco_gpu.dylib";
        }
        else
        {
            yield return "libalco_gpu.so";
        }
    }
}
