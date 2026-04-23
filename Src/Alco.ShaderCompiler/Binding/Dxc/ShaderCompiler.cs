using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Alco.ShaderCompiler;

/// <summary>
/// A delegate for shader header inclusion.
/// </summary>
/// <param name="includeName">The name of the header file being included.</param>
/// <returns>Contents of the included header file.</returns>
public delegate string FileIncludeHandler(string includeName);

/// <summary>
/// A static class that allows accessing shader compilation functionality found in the DirectXShaderCompiler.
/// </summary>
internal static class ShaderCompiler
{
    private static readonly DxcCompiler3 s_compiler;
    private static readonly DxcUtils s_utils;

    // Shared vtable for all include handler COM callbacks
    private static unsafe IntPtr* s_includeHandlerVtable;

    static ShaderCompiler()
    {
        s_utils = new DxcUtils(DXCNative.CreateInstance(DxcGuids.CLSID_DxcUtils, DxcGuids.IID_IDxcUtils));
        s_compiler = new DxcCompiler3(DXCNative.CreateInstance(DxcGuids.CLSID_DxcCompiler, DxcGuids.IID_IDxcCompiler3));
    }

    /// <summary>
    /// Compiles a string of shader code.
    /// </summary>
    /// <param name="code">The code to compile.</param>
    /// <param name="compilationOptions">The options to compile with.</param>
    /// <param name="includeHandler">The include handler to use for header inclusion.</param>
    /// <returns>A CompilationResult structure containing the resulting bytecode and possible errors.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static CompilationResult Compile(string code, CompilerOptions compilationOptions, FileIncludeHandler? includeHandler = null)
    {
        return Compile(code, compilationOptions.GetArgumentsArray(), includeHandler);
    }

    /// <summary>
    /// Compiles a string of shader code.
    /// </summary>
    /// <param name="code">The code to compile.</param>
    /// <param name="compilerArgs">The array of string arguments to compile the shader with.</param>
    /// <param name="includeHandler">The include handler to use for header inclusion.</param>
    /// <returns>A CompilationResult structure containing the resulting bytecode and possible errors.</returns>
    public static unsafe CompilationResult Compile(string code, string[] compilerArgs, FileIncludeHandler? includeHandler = null)
    {
        byte[] sourceBytes = Encoding.UTF8.GetBytes(code);
        IntPtr sourcePtr = Marshal.AllocHGlobal(sourceBytes.Length);
        Marshal.Copy(sourceBytes, 0, sourcePtr, sourceBytes.Length);
        DxcBuffer buffer = new()
        {
            Ptr = sourcePtr,
            Size = (nuint)sourceBytes.Length,
            Encoding = DxcGuids.DXC_CP_UTF8
        };

        DxcPinnedStringArray argsArray = default;
        IntPtr includeHandlerPtr = IntPtr.Zero;
        GCHandle includeHandlerGc = default;

        try
        {
            if (compilerArgs.Length > 0)
                argsArray = new DxcPinnedStringArray(compilerArgs);

            if (includeHandler != null)
            {
                includeHandlerPtr = CreateIncludeHandler(includeHandler, out includeHandlerGc);
            }

            int hr = s_compiler.Compile(
                ref buffer,
                argsArray.Handle,
                (uint)compilerArgs.Length,
                includeHandlerPtr,
                DxcGuids.IID_IDxcResult,
                out IntPtr resultPtr);

            if (hr < 0 && resultPtr == IntPtr.Zero)
            {
                return new CompilationResult
                {
                    objectBytes = [],
                    compilationErrors = $"DXC compilation failed with HRESULT 0x{hr:X8}"
                };
            }

            return ExtractResult(resultPtr);
        }
        finally
        {
            if (sourcePtr != IntPtr.Zero)
                Marshal.FreeHGlobal(sourcePtr);
            argsArray.Release();
            if (includeHandlerPtr != IntPtr.Zero)
                FreeIncludeHandler(includeHandlerPtr, includeHandlerGc);
        }
    }

    private static unsafe CompilationResult ExtractResult(IntPtr resultPtr)
    {
        var result = new DxcResult(resultPtr);

        try
        {
            byte[] objectBytes = [];
            string? compilationErrors = null;

            if (result.HasOutput(DxcOutKind.Errors))
            {
                IntPtr errorsPtr = result.GetOutput(DxcOutKind.Errors, DxcGuids.IID_IDxcBlobUtf8);
                if (errorsPtr != IntPtr.Zero)
                {
                    var errorsBlob = new DxcBlobUtf8(errorsPtr);
                    string errors = errorsBlob.GetString();
                    errorsBlob.Release();
                    if (errors.Length > 0)
                        compilationErrors = errors;
                }
            }

            if (result.HasOutput(DxcOutKind.Object))
            {
                IntPtr blobPtr = result.GetOutput(DxcOutKind.Object, DxcGuids.IID_IDxcBlob);
                if (blobPtr != IntPtr.Zero)
                {
                    var blob = new DxcBlob(blobPtr);
                    objectBytes = blob.ToArray();
                    blob.Release();
                }
            }

            return new CompilationResult
            {
                objectBytes = objectBytes,
                compilationErrors = compilationErrors
            };
        }
        finally
        {
            result.Release();
        }
    }

    #region IDxcIncludeHandler COM Callback

    private static unsafe IntPtr CreateIncludeHandler(FileIncludeHandler handler, out GCHandle gcHandle)
    {
        IntPtr* vtable = GetOrCreateIncludeHandlerVtable();

        // Allocate COM object: [vtable_ptr] [gc_handle]
        IntPtr* comObject = (IntPtr*)NativeMemory.Alloc(2, (nuint)IntPtr.Size);
        comObject[0] = (IntPtr)vtable;
        gcHandle = GCHandle.Alloc(handler);
        comObject[1] = GCHandle.ToIntPtr(gcHandle);

        return (IntPtr)comObject;
    }

    private static unsafe void FreeIncludeHandler(IntPtr comObjectPtr, GCHandle gcHandle)
    {
        if (gcHandle.IsAllocated)
            gcHandle.Free();
        NativeMemory.Free((void*)comObjectPtr);
    }

    private static unsafe IntPtr* GetOrCreateIncludeHandlerVtable()
    {
        if (s_includeHandlerVtable != null)
            return s_includeHandlerVtable;

        s_includeHandlerVtable = (IntPtr*)NativeMemory.Alloc(4, (nuint)IntPtr.Size);
        s_includeHandlerVtable[0] = (IntPtr)(delegate* unmanaged[Stdcall]<IntPtr, Guid*, IntPtr*, int>)&IncludeHandler_QI;
        s_includeHandlerVtable[1] = (IntPtr)(delegate* unmanaged[Stdcall]<IntPtr, uint>)&IncludeHandler_AddRef;
        s_includeHandlerVtable[2] = (IntPtr)(delegate* unmanaged[Stdcall]<IntPtr, uint>)&IncludeHandler_Release;
        s_includeHandlerVtable[3] = (IntPtr)(delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr*, int>)&IncludeHandler_LoadSource;
        return s_includeHandlerVtable;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static unsafe int IncludeHandler_QI(IntPtr thisPtr, Guid* riid, IntPtr* ppvObject)
    {
        *ppvObject = thisPtr;
        return 0; // S_OK
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static uint IncludeHandler_AddRef(IntPtr thisPtr) => 2;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static uint IncludeHandler_Release(IntPtr thisPtr) => 1;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static unsafe int IncludeHandler_LoadSource(IntPtr thisPtr, IntPtr filename, IntPtr* ppIncludeSource)
    {
        try
        {
            *ppIncludeSource = IntPtr.Zero;

            if (filename == IntPtr.Zero)
                return unchecked((int)0x80070057); // E_INVALIDARG

            // Extract the managed delegate from the COM object context
            IntPtr* comObj = (IntPtr*)thisPtr;
            GCHandle handle = GCHandle.FromIntPtr(comObj[1]);
            FileIncludeHandler handler = (FileIncludeHandler)handle.Target!;

            string filenameStr = PtrToStringWChar(filename)!;
            string content = handler(filenameStr);

            // Create an IDxcBlob via IDxcUtils::CreateBlob (copies the data)
            byte[] contentBytes = Encoding.UTF8.GetBytes(content);
            IntPtr contentPtr = Marshal.AllocHGlobal(contentBytes.Length);
            Marshal.Copy(contentBytes, 0, contentPtr, contentBytes.Length);
            try
            {
                return s_utils.CreateBlob(contentPtr, (uint)contentBytes.Length, DxcGuids.DXC_CP_UTF8, out *ppIncludeSource);
            }
            finally
            {
                Marshal.FreeHGlobal(contentPtr);
            }
        }
        catch
        {
            return unchecked((int)0x80004005); // E_FAIL
        }
    }

    #endregion

    /// <summary>
    /// Reads a null-terminated wchar_t* string. UTF-16 on Windows, UTF-32 on Linux/macOS.
    /// </summary>
    private static unsafe string? PtrToStringWChar(IntPtr ptr)
    {
        if (ptr == IntPtr.Zero) return null;

        if (OperatingSystem.IsWindows())
        {
            return Marshal.PtrToStringUni(ptr);
        }
        else
        {
            // wchar_t is 4 bytes (UTF-32) on Linux/macOS
            uint* p = (uint*)ptr;
            int len = 0;
            while (p[len] != 0) len++;
            char* chars = stackalloc char[len];
            for (int i = 0; i < len; i++)
                chars[i] = (char)p[i];
            return new string(chars, 0, len);
        }
    }
}
