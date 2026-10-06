using System.Runtime.InteropServices;

namespace Alco.Graphics.AlcoGpu.Interop;

/// <summary>Resolves the alco-gpu library from the application directory before default probing.</summary>
internal static class AlcoGpuNativeLibrary
{
    private static readonly Lock LoadLock = new();
    private static nint _library;
    private static bool _resolverRegistered;

    /// <summary>Loads the library once and registers the resolver only after a successful load.</summary>
    public static void EnsureLoaded()
    {
        lock (LoadLock)
        {
            if (_resolverRegistered)
            {
                return;
            }

            string fileName = OperatingSystem.IsWindows() ? "alco_gpu.dll"
                : OperatingSystem.IsMacOS() ? "libalco_gpu.dylib" : "libalco_gpu.so";
            if (_library == 0 && !NativeLibrary.TryLoad(Path.Combine(AppContext.BaseDirectory, fileName), out _library))
            {
                // A failed load remains retryable; never publish initialized state early.
                _library = NativeLibrary.Load("alco_gpu", typeof(AlcoGpuNativeLibrary).Assembly, null);
            }

            NativeLibrary.SetDllImportResolver(typeof(AlcoGpuNativeLibrary).Assembly,
                static (name, assembly, searchPath) => name == "alco_gpu" ? _library : 0);
            _resolverRegistered = true;
        }
    }
}
