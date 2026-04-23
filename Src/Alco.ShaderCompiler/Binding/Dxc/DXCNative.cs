using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace DirectXShaderCompiler.NET;

[StructLayout(LayoutKind.Sequential)]
internal struct DxcBuffer
{
    public IntPtr Ptr;
    public nuint Size;
    public uint Encoding;
}

internal enum DxcOutKind
{
    None = 0,
    Object = 1,
    Errors = 2,
}

internal static class DxcGuids
{
    public static readonly Guid CLSID_DxcCompiler = new("73e22d93-e6ce-47f3-b5bf-f0664f39c1b0");
    public static readonly Guid CLSID_DxcUtils = new("6245d6af-66e0-48fd-80b4-4d271796748c");
    public static readonly Guid IID_IDxcCompiler3 = new("228B4687-5A6A-4730-900C-9702B2203F54");
    public static readonly Guid IID_IDxcResult = new("58346CDA-DDE7-4497-9461-6F87AF5E0659");
    public static readonly Guid IID_IDxcBlobUtf8 = new("3DA636C9-BA71-4024-A301-30CBF125305B");
    public static readonly Guid IID_IDxcUtils = new("4605C4CB-2019-492A-ADA4-65F20BB7D67F");

    public const uint DXC_CP_ACP = 0;
}

internal static class DXCNative
{
    [DllImport("dxcompiler", EntryPoint = "DxcCreateInstance", CallingConvention = CallingConvention.StdCall)]
    private static unsafe extern int DxcCreateInstance_(void* rclsid, void* riid, void* ppv);

    /// <summary>
    /// Creates a COM object instance via DXC's DxcCreateInstance factory.
    /// </summary>
    public static unsafe IntPtr CreateInstance(Guid clsid, Guid iid)
    {
        IntPtr ptr;
        int hr = DxcCreateInstance_(&clsid, &iid, &ptr);
        if (hr < 0)
            throw new COMException($"DxcCreateInstance failed for {clsid}", hr);
        return ptr;
    }
}

internal static class Com
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe IntPtr Vcall(IntPtr nativePtr, int index) => (*(IntPtr**)nativePtr)[index];

    public static unsafe void Release(IntPtr nativePtr)
    {
        ((delegate* unmanaged[Stdcall]<IntPtr, uint>)Vcall(nativePtr, 2))(nativePtr);
    }
}

/// <summary>
/// IDxcBlob COM wrapper — provides access to compiled shader bytecode.
/// </summary>
internal sealed class DxcBlob
{
    public IntPtr NativePointer { get; }
    public DxcBlob(IntPtr nativePointer) => NativePointer = nativePointer;

    /// <summary>Gets a pointer to the blob data.</summary>
    public unsafe IntPtr GetBufferPointer() =>
        ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr>)Com.Vcall(NativePointer, 3))(NativePointer);

    /// <summary>Gets the size of the blob data in bytes.</summary>
    public unsafe nuint GetBufferSize() =>
        ((delegate* unmanaged[Stdcall]<IntPtr, nuint>)Com.Vcall(NativePointer, 4))(NativePointer);

    /// <summary>Copies the blob data to a managed byte array.</summary>
    public byte[] ToArray()
    {
        unsafe
        {
            nuint size = GetBufferSize();
            byte[] result = new byte[(int)size];
            Marshal.Copy(GetBufferPointer(), result, 0, (int)size);
            return result;
        }
    }

    public void Release() => Com.Release(NativePointer);
}

/// <summary>
/// IDxcBlobUtf8 COM wrapper — provides access to UTF-8 error strings.
/// </summary>
internal sealed class DxcBlobUtf8
{
    public IntPtr NativePointer { get; }
    public DxcBlobUtf8(IntPtr nativePointer) => NativePointer = nativePointer;

    /// <summary>Gets the error string content.</summary>
    public unsafe string GetString()
    {
        IntPtr ptr = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr>)Com.Vcall(NativePointer, 6))(NativePointer);
        return Marshal.PtrToStringUTF8(ptr)!;
    }

    public void Release() => Com.Release(NativePointer);
}

/// <summary>
/// IDxcResult COM wrapper — provides access to compilation outputs.
/// </summary>
/// <remarks>
/// Vtable layout (inherits IDxcOperationResult which inherits IUnknown):
/// [0] QI, [1] AddRef, [2] Release, [3] GetStatus, [4] GetResult, [5] GetErrorBuffer,
/// [6] HasOutput, [7] GetOutput
/// </remarks>
internal sealed class DxcResult
{
    public IntPtr NativePointer { get; }
    public DxcResult(IntPtr nativePointer) => NativePointer = nativePointer;

    /// <summary>Checks whether a specific output kind is available.</summary>
    public unsafe bool HasOutput(DxcOutKind kind)
    {
        int result = ((delegate* unmanaged[Stdcall]<IntPtr, int, int>)Com.Vcall(NativePointer, 6))(NativePointer, (int)kind);
        return result != 0;
    }

    /// <summary>Gets an output by kind and IID. Returns the raw native pointer.</summary>
    public unsafe IntPtr GetOutput(DxcOutKind kind, Guid iid)
    {
        IntPtr outputPtr = IntPtr.Zero;
        IntPtr outputName = IntPtr.Zero;
        // IDxcResult::GetOutput(DxcOutKind, REFIID, void**, IDxcBlobWide**)
        ((delegate* unmanaged[Stdcall]<IntPtr, int, void*, void*, void*, int>)Com.Vcall(NativePointer, 7))(
            NativePointer, (int)kind, &iid, &outputPtr, &outputName);
        if (outputName != IntPtr.Zero)
            Com.Release(outputName);
        return outputPtr;
    }

    public void Release() => Com.Release(NativePointer);
}

/// <summary>
/// IDxcCompiler3 COM wrapper — the main shader compilation interface.
/// </summary>
/// <remarks>
/// Vtable layout (inherits IUnknown): [0] QI, [1] AddRef, [2] Release, [3] Compile, [4] Disassemble
/// </remarks>
internal sealed class DxcCompiler3
{
    public IntPtr NativePointer { get; }
    public DxcCompiler3(IntPtr nativePointer) => NativePointer = nativePointer;

    /// <summary>
    /// Compiles shader source with the given arguments.
    /// </summary>
    public unsafe int Compile(ref DxcBuffer source, IntPtr arguments, uint argCount, IntPtr includeHandler, Guid riid, out IntPtr result)
    {
        fixed (void* resultPtr = &result)
        fixed (void* sourcePtr = &source)
        {
            return ((delegate* unmanaged[Stdcall]<IntPtr, void*, void*, uint, void*, void*, void*, int>)Com.Vcall(NativePointer, 3))(
                NativePointer, sourcePtr, (void*)arguments, argCount, (void*)includeHandler, &riid, resultPtr);
        }
    }

    public void Release() => Com.Release(NativePointer);
}

/// <summary>
/// IDxcUtils COM wrapper — utility methods for blob creation.
/// </summary>
/// <remarks>
/// Vtable layout (inherits IUnknown):
/// [0] QI, [1] AddRef, [2] Release,
/// [3] CreateBlobFromBlob, [4] CreateBlobFromPinned, [5] MoveToBlob,
/// [6] CreateBlob, [7] LoadFile, [8] CreateReadOnlyStreamFromBlob,
/// [9] CreateDefaultIncludeHandler, ...
/// </remarks>
internal sealed class DxcUtils
{
    public IntPtr NativePointer { get; }
    public DxcUtils(IntPtr nativePointer) => NativePointer = nativePointer;

    /// <summary>
    /// Creates a blob from a copy of the given data.
    /// IDxcUtils::CreateBlob(data, size, codePage, out IDxcBlobEncoding)
    /// </summary>
    public unsafe int CreateBlob(IntPtr data, uint size, uint codePage, out IntPtr blobEncoding)
    {
        blobEncoding = IntPtr.Zero;
        return ((delegate* unmanaged[Stdcall]<IntPtr, void*, uint, uint, void*, int>)Com.Vcall(NativePointer, 6))(
            NativePointer, (void*)data, size, codePage, &blobEncoding);
    }

    public void Release() => Com.Release(NativePointer);
}

/// <summary>
/// Pins an array of strings as wchar_t** (UTF-16) for passing to IDxcCompiler3.Compile.
/// </summary>
internal unsafe ref struct Utf16PinnedStringArray
{
    private readonly IntPtr* _handle;
    public readonly int Length;

    public Utf16PinnedStringArray(string[] strings)
    {
        Length = strings.Length;
        _handle = (IntPtr*)NativeMemory.Alloc((nuint)(Length * IntPtr.Size));

        for (int i = 0; i < Length; i++)
        {
            int charCount = strings[i].Length;
            uint byteCount = (uint)((charCount + 1) * sizeof(char));
            char* dst = (char*)NativeMemory.Alloc(byteCount);

            if (charCount > 0)
            {
                fixed (char* src = strings[i])
                    Unsafe.CopyBlock(dst, src, (uint)(charCount * sizeof(char)));
            }
            dst[charCount] = '\0';
            _handle[i] = (IntPtr)dst;
        }
    }

    public IntPtr Handle => (IntPtr)_handle;

    public void Release()
    {
        if (Length == 0) return;
        for (int i = 0; i < Length; i++)
            NativeMemory.Free((void*)_handle[i]);
        NativeMemory.Free(_handle);
    }
}
