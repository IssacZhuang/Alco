using System.Runtime.InteropServices;
using NUnit.Framework;

namespace Alco.Graphics.Test;

/// <summary>
/// The metallib passthrough probe: the wgpu-native library must export
/// wgpuDeviceCreateShaderModuleMetalLib (third Alco patch, v29.0.1.1-alco.3+).
/// wgpuGetProcAddress itself is an unimplemented stub upstream, so the engine
/// probes with NativeLibrary.GetExport on the already-loaded library — this
/// test mirrors that exact path and doubles as a manifest-drift canary: an
/// older DLL in the runtimes folder turns this red before any Metal machine
/// silently falls back to MSL.
/// </summary>
[TestFixture]
public sealed class MetalLibExportProbeTests
{
    [Test]
    public void NativeLibraryGetExport_ResolvesMetalLibEntryPoint()
    {
        nint handle = NativeLibrary.Load("wgpu_native");
        try
        {
            Assert.That(NativeLibrary.TryGetExport(handle, "wgpuDeviceCreateShaderModuleMetalLib", out nint _),
                Is.True,
                "wgpu-native has no wgpuDeviceCreateShaderModuleMetalLib export; " +
                "update the runtimes to v29.0.1.1-alco.3+");
        }
        finally
        {
            NativeLibrary.Free(handle);
        }
    }
}
