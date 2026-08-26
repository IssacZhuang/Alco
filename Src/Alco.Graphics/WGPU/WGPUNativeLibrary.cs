using System.Runtime.InteropServices;

namespace Alco.Graphics.WebGPU;

/// <summary>
/// Loads the wgpu-native shared library by probing the application directory
/// with platform-mapped file names. NativeLibrary's simple-name probe only
/// resolves "wgpu_native" on Windows: on Linux and macOS dlopen does not search
/// the application directory and needs the lib prefix and platform extension,
/// so the bare name fails even though the DllImport bindings load fine.
/// </summary>
internal static class WGPUNativeLibrary
{
    public static bool TryLoad(out nint handle)
    {
        foreach (string fileName in CandidateFileNames())
        {
            string path = Path.Combine(AppContext.BaseDirectory, fileName);
            if (NativeLibrary.TryLoad(path, out handle))
            {
                return true;
            }
        }
        // Fall back to the default probe (system library paths).
        return NativeLibrary.TryLoad("wgpu_native", out handle);
    }

    private static IEnumerable<string> CandidateFileNames()
    {
        if (OperatingSystem.IsWindows())
        {
            yield return "wgpu_native.dll";
        }
        else if (OperatingSystem.IsMacOS())
        {
            yield return "libwgpu_native.dylib";
        }
        else
        {
            yield return "libwgpu_native.so";
        }
    }
}
