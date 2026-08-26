using System.Runtime.CompilerServices;

namespace Alco.ShaderCompiler;

// COM-shape interop helpers shared by the slang vtable bindings
// (the hand-rolled COM ABI of the pinned Slang runtime).
internal static class Com
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe IntPtr Vcall(IntPtr nativePtr, int index) => (*(IntPtr**)nativePtr)[index];

    public static unsafe void Release(IntPtr nativePtr)
    {
        ((delegate* unmanaged[Stdcall]<IntPtr, uint>)Vcall(nativePtr, 2))(nativePtr);
    }
}
