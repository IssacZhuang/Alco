using System.Runtime.InteropServices;
using NUnit.Framework;

namespace Alco.ShaderCompiler;

/// <summary>
/// slang's DXIL pass-through delegates to the external 'dxc' downstream
/// compiler. The runtimes folder ships slang and dxil.dll but not
/// dxcompiler.dll, so CI runners (which carry no DXC install) cannot compile
/// DXIL: DXIL tests run where dxcompiler is loadable and skip elsewhere.
/// </summary>
internal static class DxilDownstreamAvailability
{
    public static void AssertAvailable()
    {
        if (!NativeLibrary.TryLoad("dxcompiler", out nint handle))
        {
            Assert.Ignore(
                "slang's DXIL pass-through needs the 'dxc' downstream compiler (dxcompiler), " +
                "which is not loadable on this machine. Install DirectXShaderCompiler " +
                "(https://github.com/microsoft/DirectXShaderCompiler) and put dxcompiler on PATH to run this test.");
        }
        NativeLibrary.Free(handle);
    }
}
