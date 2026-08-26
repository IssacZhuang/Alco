using System.Runtime.InteropServices;

namespace Alco.ShaderCompiler;

// ─────────────────────────────────────────────────────────────────────────────
// Hand-rolled COM vtable wrappers over slang's modern API
// (IGlobalSession / ISession / IModule / IComponentType / IEntryPoint),
// with raw pointers and vtable slots verified against the pinned slang headers:
// vtable slot indices, C# 9 function pointers, manual Release().
//
// Vtable layouts verified against slang.h of the pinned release (2026.16);
// slots are annotated per interface. Only the methods the engine uses are
// surfaced.
// ─────────────────────────────────────────────────────────────────────────────

[StructLayout(LayoutKind.Sequential)]
internal struct SlangPreprocessorMacroDesc
{
    public IntPtr Name;   // char* (UTF-8)
    public IntPtr Value;  // char* (UTF-8)
}

[StructLayout(LayoutKind.Sequential)]
internal struct SlangCompilerOptionValue
{
    public int Kind;          // slang::CompilerOptionValueKind: Int=0, String=1
    public int IntValue0;
    public int IntValue1;
    public IntPtr StringValue0;
    public IntPtr StringValue1;
}

[StructLayout(LayoutKind.Sequential)]
internal struct SlangCompilerOptionEntry
{
    public int Name;                        // slang::CompilerOptionName
    public SlangCompilerOptionValue Value;
}

/// <summary>Slang TargetDesc (x64 layout, see slang.h; Sequential supplies the natural alignment).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct SlangTargetDesc
{
    public nuint StructureSize;
    public int Format;                      // SlangCompileTarget
    public int Profile;                     // SlangProfileID
    public uint Flags;                      // SlangTargetFlags
    public int FloatingPointMode;
    public int LineDirectiveMode;
    private byte _forceGLSLScalarBufferLayout;
    public unsafe SlangCompilerOptionEntry* CompilerOptionEntries;
    public uint CompilerOptionEntryCount;

    public static unsafe SlangTargetDesc Create(int format)
    {
        return new SlangTargetDesc
        {
            StructureSize = (nuint)sizeof(SlangTargetDesc),
            Format = format,
        };
    }
}

/// <summary>Slang SessionDesc (x64 layout, see slang.h; Sequential supplies the natural alignment).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct SlangSessionDesc
{
    public nuint StructureSize;
    public unsafe SlangTargetDesc* Targets;
    public nint TargetCount;
    public uint Flags;                      // SessionFlags
    public int DefaultMatrixLayoutMode;
    public unsafe IntPtr* SearchPaths;      // char* const*
    public nint SearchPathCount;
    public unsafe SlangPreprocessorMacroDesc* PreprocessorMacros;
    public nint PreprocessorMacroCount;
    public IntPtr FileSystem;               // ISlangFileSystem*
    public byte EnableEffectAnnotations;
    public byte AllowGLSLSyntax;
    public unsafe SlangCompilerOptionEntry* CompilerOptionEntries;
    public uint CompilerOptionEntryCount;
    public byte SkipSPIRVValidation;

    public static unsafe SlangSessionDesc Create()
    {
        return new SlangSessionDesc
        {
            StructureSize = (nuint)sizeof(SlangSessionDesc),
        };
    }
}

/// <summary>slang::SpecializationArg — type or expression argument (a union).</summary>
[StructLayout(LayoutKind.Explicit)]
internal struct SlangSpecializationArg
{
    [FieldOffset(0)] public int Kind;       // Unknown=0, Type=1, Expr=2
    [FieldOffset(8)] private IntPtr _value; // union { TypeReflection* type; const char* expr; }

    public static SlangSpecializationArg FromType(IntPtr type) => new() { Kind = 1, _value = type };
    public static SlangSpecializationArg FromExpr(IntPtr expr) => new() { Kind = 2, _value = expr };
}

/// <summary>ISlangBlob wrapper (layout-compatible with IDxcBlob).</summary>
/// <remarks>
/// Vtable slot mapping: 0-2 IUnknown, 3 getBufferPointer, 4 getBufferSize.
/// </remarks>
internal sealed class SlangBlob
{
    public IntPtr NativePointer { get; }
    public SlangBlob(IntPtr nativePointer) => NativePointer = nativePointer;

    public unsafe IntPtr GetBufferPointer() =>
        ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr>)Com.Vcall(NativePointer, 3))(NativePointer);

    public unsafe nuint GetBufferSize() =>
        ((delegate* unmanaged[Stdcall]<IntPtr, nuint>)Com.Vcall(NativePointer, 4))(NativePointer);

    public byte[] ToArray()
    {
        unsafe
        {
            IntPtr ptr = GetBufferPointer();
            int size = (int)GetBufferSize();
            if (ptr == IntPtr.Zero || size == 0)
                return [];
            byte[] result = new byte[size];
            Marshal.Copy(ptr, result, 0, size);
            return result;
        }
    }

    /// <summary>The blob content as UTF-8 text (diagnostics).</summary>
    public string? GetText()
    {
        byte[] bytes = ToArray();
        return bytes.Length == 0 ? null : System.Text.Encoding.UTF8.GetString(bytes);
    }

    public void Release() => Com.Release(NativePointer);
}

/// <summary>
/// IGlobalSession wrapper. Created once per process via slang_createGlobalSession
/// (SlangCompiler holds it for the process lifetime); owns the slang core module.
/// </summary>
/// <remarks>
/// Vtable slot mapping: 0-2 IUnknown, 3 createSession, 4 findProfile, 8 getBuildTagString,
/// 17 checkCompileTargetSupport, 22 findCapability.
/// </remarks>
internal sealed class SlangGlobalSession
{
    public IntPtr NativePointer { get; }

    internal SlangGlobalSession(IntPtr nativePointer) => NativePointer = nativePointer;

    public static SlangGlobalSession Create()
    {
        int hr = SlangNative.slang_createGlobalSession(0, out IntPtr ptr);
        if (hr < 0 || ptr == IntPtr.Zero)
            throw new InvalidOperationException($"slang_createGlobalSession failed with HRESULT 0x{hr:X8}.");
        return new SlangGlobalSession(ptr);
    }

    public unsafe SlangSession CreateSession(in SlangSessionDesc desc)
    {
        SlangSessionDesc local = desc;
        IntPtr session;
        int hr = ((delegate* unmanaged[Stdcall]<IntPtr, SlangSessionDesc*, IntPtr*, int>)Com.Vcall(NativePointer, 3))(
            NativePointer, &local, &session);
        if (hr < 0 || session == IntPtr.Zero)
            throw new InvalidOperationException($"slang createSession failed with HRESULT 0x{hr:X8}.");
        return new SlangSession(session);
    }

    public unsafe string GetBuildTagString()
    {
        IntPtr ptr = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr>)Com.Vcall(NativePointer, 8))(NativePointer);
        return SlangNative.StringFromPtr(ptr) ?? "unknown";
    }

    public unsafe int FindProfile(string name)
    {
        using SlangPinnedUtf8 profileName = new(name);
        return ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int>)Com.Vcall(NativePointer, 4))(
            NativePointer, profileName.Pointer);
    }

    /// <summary>
    /// IGlobalSession::checkCompileTargetSupport — SLANG_OK when the target compiles
    /// on this machine. The metallib target additionally requires Apple's external
    /// Metal toolchain, so "supported" here means an actual compile can run.
    /// </summary>
    public unsafe bool CheckCompileTargetSupport(int target)
        => ((delegate* unmanaged[Stdcall]<IntPtr, int, int>)Com.Vcall(NativePointer, 17))(
            NativePointer, target) == SlangNative.SLANG_OK;

    public void Release() => Com.Release(NativePointer);
}

/// <summary>ISession wrapper — scope for module loading and linking.</summary>
internal sealed class SlangSession
{
    public IntPtr NativePointer { get; }
    public SlangGlobalSession GlobalSession { get; }

    internal SlangSession(IntPtr nativePointer)
    {
        NativePointer = nativePointer;
        unsafe
        {
            IntPtr global = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr>)Com.Vcall(nativePointer, 3))(nativePointer);
            GlobalSession = new SlangGlobalSession(global);
        }
    }

    // ISession: 0-2 IUnknown, 3 getGlobalSession, 4 loadModule, 5 loadModuleFromSource,
    // 6 createCompositeComponentType, 7 specializeType, 8 getTypeLayout,
    // 16 loadModuleFromIRBlob, 17 getLoadedModuleCount, 18 getLoadedModule,
    // 19 isBinaryModuleUpToDate, 20 loadModuleFromSourceString

    /// <summary>Loads (or returns the session-cached) module resolved by name through the file system.</summary>
    public unsafe SlangModule? LoadModule(string moduleName, out string? diagnostics)
    {
        using SlangPinnedUtf8 name = new(moduleName);
        IntPtr diag = IntPtr.Zero;
        IntPtr module = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr*, IntPtr>)Com.Vcall(NativePointer, 4))(
            NativePointer, name.Pointer, &diag);
        diagnostics = SlangBlobText(diag);
        return module == IntPtr.Zero ? null : new SlangModule(module);
    }

    /// <summary>Loads a module from in-memory source (a virtual file).</summary>
    public unsafe SlangModule? LoadModuleFromSource(string moduleName, string path, byte[] source, out string? diagnostics)
    {
        using SlangPinnedUtf8 name = new(moduleName);
        using SlangPinnedUtf8 pathUtf8 = new(path);
        using SlangPinnedBuffer sourceBuffer = new(source);
        IntPtr blob = SlangNative.slang_createBlob(source, (nuint)source.Length);
        if (blob == IntPtr.Zero)
            throw new InvalidOperationException("slang_createBlob failed for module source.");
        try
        {
            IntPtr diag = IntPtr.Zero;
            IntPtr module = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr*, IntPtr>)Com.Vcall(NativePointer, 5))(
                NativePointer, name.Pointer, pathUtf8.Pointer, blob, &diag);
            diagnostics = SlangBlobText(diag);
            return module == IntPtr.Zero ? null : new SlangModule(module);
        }
        finally
        {
            Com.Release(blob);
        }
    }

    public unsafe SlangComponentType CreateCompositeComponentType(ReadOnlySpan<SlangComponentType> components, out string? diagnostics)
    {
        IntPtr* nativeComponents = stackalloc IntPtr[components.Length];
        for (int i = 0; i < components.Length; i++)
            nativeComponents[i] = components[i].NativePointer;
        IntPtr composite = IntPtr.Zero;
        IntPtr diag = IntPtr.Zero;
        int hr = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, nint, IntPtr*, IntPtr*, int>)Com.Vcall(NativePointer, 6))(
            NativePointer, nativeComponents, components.Length, &composite, &diag);
        diagnostics = SlangBlobText(diag);
        if (hr < 0 || composite == IntPtr.Zero)
            throw new InvalidOperationException($"slang createCompositeComponentType failed: 0x{hr:X8} {diagnostics}");
        return new SlangComponentType(composite);
    }

    /// <summary>Loads a module from serialized slang IR (a .slang-module blob).</summary>
    public unsafe SlangModule? LoadModuleFromIRBlob(string moduleName, string path, byte[] ir, out string? diagnostics)
    {
        using SlangPinnedUtf8 name = new(moduleName);
        using SlangPinnedUtf8 pathUtf8 = new(path);
        fixed (byte* irPtr = ir)
        {
            IntPtr diag = IntPtr.Zero;
            IntPtr module = SlangNative.slang_loadModuleFromIRBlob(
                NativePointer, name.Pointer, pathUtf8.Pointer, irPtr, (nuint)ir.Length, &diag);
            diagnostics = SlangBlobText(diag);
            return module == IntPtr.Zero ? null : new SlangModule(module);
        }
    }

    /// <summary>
    /// Whether a serialized module blob is up-to-date for <paramref name="modulePath"/> under the
    /// session's options. Note: when the primary source cannot be found on the search paths the
    /// blob is reported up-to-date without validation — callers distributing source-less builds
    /// must stamp their own source-hash key.
    /// </summary>
    public unsafe bool IsBinaryModuleUpToDate(string modulePath, byte[] serializedModule)
    {
        using SlangPinnedUtf8 pathUtf8 = new(modulePath);
        IntPtr blob = SlangNative.slang_createBlob(serializedModule, (nuint)serializedModule.Length);
        if (blob == IntPtr.Zero)
            return false;
        try
        {
            return ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr, byte>)Com.Vcall(NativePointer, 19))(
                NativePointer, pathUtf8.Pointer, blob) != 0;
        }
        finally
        {
            Com.Release(blob);
        }
    }

    /// <summary>Specializes an unspecialized component type with concrete arguments.</summary>
    public static unsafe SlangComponentType Specialize(SlangComponentType component, ReadOnlySpan<SlangSpecializationArg> args, out string? diagnostics)
    {
        fixed (SlangSpecializationArg* argPtr = args)
        {
            IntPtr specialized = IntPtr.Zero;
            IntPtr diag = IntPtr.Zero;
            int hr = ((delegate* unmanaged[Stdcall]<IntPtr, SlangSpecializationArg*, nint, IntPtr*, IntPtr*, int>)Com.Vcall(component.NativePointer, 9))(
                component.NativePointer, argPtr, args.Length, &specialized, &diag);
            diagnostics = SlangBlobText(diag);
            if (hr < 0 || specialized == IntPtr.Zero)
                throw new InvalidOperationException($"slang specialize failed: 0x{hr:X8} {diagnostics}");
            return new SlangComponentType(specialized);
        }
    }

    private static string? SlangBlobText(IntPtr blobPtr)
    {
        if (blobPtr == IntPtr.Zero)
            return null;
        try
        {
            return new SlangBlob(blobPtr).GetText();
        }
        finally
        {
            Com.Release(blobPtr);
        }
    }

    public void Release() => Com.Release(NativePointer);
}

/// <summary>IComponentType wrapper — composites, entry points, specialized and linked programs.</summary>
internal sealed class SlangComponentType
{
    public IntPtr NativePointer { get; }

    public SlangComponentType(IntPtr nativePointer) => NativePointer = nativePointer;

    // IComponentType: 0-2 IUnknown, 3 getSession, 4 getLayout, 5 getSpecializationParamCount,
    // 6 getEntryPointCode, 7 getResultAsFileSystem, 8 getEntryPointHash, 9 specialize,
    // 10 link, 11 getEntryPointHostCallable, 12 renameEntryPoint, 13 linkWithOptions,
    // 14 getTargetCode, 15 getTargetMetadata, 16 getEntryPointMetadata

    public unsafe SlangSession Session => new(
        ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr>)Com.Vcall(NativePointer, 3))(NativePointer));

    /// <summary>ProgramLayout pointer (SlangReflection*) for target 0, or null on failure.</summary>
    public unsafe IntPtr GetLayout(out string? diagnostics)
    {
        IntPtr diag = IntPtr.Zero;
        IntPtr layout = ((delegate* unmanaged[Stdcall]<IntPtr, nint, IntPtr*, IntPtr>)Com.Vcall(NativePointer, 4))(
            NativePointer, 0, &diag);
        diagnostics = DiagText(diag);
        return layout;
    }

    private static string? DiagText(IntPtr diag)
    {
        if (diag == IntPtr.Zero)
            return null;
        try
        {
            return new SlangBlob(diag).GetText();
        }
        finally
        {
            Com.Release(diag);
        }
    }

    public unsafe nint SpecializationParamCount =>
        ((delegate* unmanaged[Stdcall]<IntPtr, nint>)Com.Vcall(NativePointer, 5))(NativePointer);

    /// <summary>Compiled code (SPIR-V) for one entry point of a fully specialized, linked program.</summary>
    public unsafe byte[] GetEntryPointCode(int entryPointIndex, out string? diagnostics)
    {
        IntPtr code = IntPtr.Zero;
        IntPtr diag = IntPtr.Zero;
        int hr = ((delegate* unmanaged[Stdcall]<IntPtr, nint, nint, IntPtr*, IntPtr*, int>)Com.Vcall(NativePointer, 6))(
            NativePointer, entryPointIndex, 0, &code, &diag);
        diagnostics = DiagText(diag);
        if (diag != IntPtr.Zero)
            Com.Release(diag);
        if (hr < 0 || code == IntPtr.Zero)
            throw new InvalidOperationException($"slang getEntryPointCode({entryPointIndex}) failed: 0x{hr:X8} {diagnostics}");
        try
        {
            return new SlangBlob(code).ToArray();
        }
        finally
        {
            Com.Release(code);
        }
    }

    public unsafe SlangComponentType Link(out string? diagnostics)
    {
        IntPtr linked = IntPtr.Zero;
        IntPtr diag = IntPtr.Zero;
        int hr = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, IntPtr*, int>)Com.Vcall(NativePointer, 10))(
            NativePointer, &linked, &diag);
        diagnostics = DiagText(diag);
        if (diag != IntPtr.Zero)
            Com.Release(diag);
        if (hr < 0 || linked == IntPtr.Zero)
            throw new InvalidOperationException($"slang link failed: 0x{hr:X8} {diagnostics}");
        return new SlangComponentType(linked);
    }

    public void Release() => Com.Release(NativePointer);
}

/// <summary>IEntryPoint wrapper — an entry point extracted from a module.</summary>
internal sealed class SlangEntryPoint
{
    public IntPtr NativePointer { get; }

    public SlangEntryPoint(IntPtr nativePointer) => NativePointer = nativePointer;

    public SlangComponentType AsComponentType() => new(NativePointer);

    public void Release() => Com.Release(NativePointer);
}

/// <summary>IModule wrapper — one compiled translation unit plus its import graph.</summary>
internal sealed class SlangModule
{
    public IntPtr NativePointer { get; }

    public SlangModule(IntPtr nativePointer) => NativePointer = nativePointer;

    // IModule (after IComponentType's 0-16): 17 findEntryPointByName,
    // 18 getDefinedEntryPointCount, 19 getDefinedEntryPoint, 20 serialize,
    // 21 writeToFile, 22 getName, 23 getFilePath, 24 getUniqueIdentity,
    // 25 findAndCheckEntryPoint, 26 getDependencyFileCount, 27 getDependencyFilePath,
    // 28 getModuleReflection, 29 disassemble

    public unsafe SlangComponentType AsComponentType() => new(NativePointer);

    public unsafe int DefinedEntryPointCount =>
        (int)((delegate* unmanaged[Stdcall]<IntPtr, int>)Com.Vcall(NativePointer, 18))(NativePointer);

    public unsafe SlangEntryPoint? GetDefinedEntryPoint(int index)
    {
        IntPtr ep = IntPtr.Zero;
        int hr = ((delegate* unmanaged[Stdcall]<IntPtr, int, IntPtr*, int>)Com.Vcall(NativePointer, 19))(
            NativePointer, index, &ep);
        return hr >= 0 && ep != IntPtr.Zero ? new SlangEntryPoint(ep) : null;
    }

    /// <summary>
    /// The module's serialized IR (a .slang-module blob): a checked, front-end-compiled
    /// translation unit that <c>loadModuleFromIRBlob</c> can restore without re-parsing.
    /// </summary>
    public unsafe byte[]? Serialize()
    {
        IntPtr blob = IntPtr.Zero;
        int hr = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)Com.Vcall(NativePointer, 20))(
            NativePointer, &blob);
        if (hr < 0 || blob == IntPtr.Zero)
            return null;
        try
        {
            return new SlangBlob(blob).ToArray();
        }
        finally
        {
            Com.Release(blob);
        }
    }

    /// <summary>Finds an entry point by name and validates it for the requested stage.</summary>
    public unsafe SlangEntryPoint? FindAndCheckEntryPoint(string name, int stage, out string? diagnostics)
    {
        using SlangPinnedUtf8 nameUtf8 = new(name);
        IntPtr ep = IntPtr.Zero;
        IntPtr diag = IntPtr.Zero;
        int hr = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int, IntPtr*, IntPtr*, int>)Com.Vcall(NativePointer, 25))(
            NativePointer, nameUtf8.Pointer, stage, &ep, &diag);
        diagnostics = diag == IntPtr.Zero ? null : new SlangBlob(diag).GetText();
        if (diag != IntPtr.Zero)
            Com.Release(diag);
        return hr >= 0 && ep != IntPtr.Zero ? new SlangEntryPoint(ep) : null;
    }

    public unsafe string? Name =>
        SlangNative.StringFromPtr(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr>)Com.Vcall(NativePointer, 22))(NativePointer));

    public unsafe string? FilePath =>
        SlangNative.StringFromPtr(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr>)Com.Vcall(NativePointer, 23))(NativePointer));

    public unsafe int DependencyFileCount =>
        (int)((delegate* unmanaged[Stdcall]<IntPtr, int>)Com.Vcall(NativePointer, 26))(NativePointer);

    /// <summary>
    /// The module's declaration tree (a <c>DeclReflection*</c>, slot 28): its child
    /// declarations — structs, functions, generic containers — the entry point of
    /// module-scope type discovery. Valid for the session's lifetime (like the module).
    /// </summary>
    public unsafe IntPtr GetModuleReflectionDecl()
        => ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr>)Com.Vcall(NativePointer, 28))(NativePointer);

    public unsafe string? GetDependencyFilePath(int index) =>
        SlangNative.StringFromPtr(((delegate* unmanaged[Stdcall]<IntPtr, int, IntPtr>)Com.Vcall(NativePointer, 27))(NativePointer, index));

    public void Release() => Com.Release(NativePointer);
}

/// <summary>Pins a UTF-8 encoded string (with NUL terminator) for the duration of a native call.</summary>
internal readonly unsafe struct SlangPinnedUtf8 : IDisposable
{
    public IntPtr Pointer { get; }
    private readonly byte[] _buffer;
    private readonly GCHandle _handle;

    public SlangPinnedUtf8(string text)
    {
        _buffer = new byte[System.Text.Encoding.UTF8.GetByteCount(text) + 1];
        System.Text.Encoding.UTF8.GetBytes(text, 0, text.Length, _buffer, 0);
        _handle = GCHandle.Alloc(_buffer, GCHandleType.Pinned);
        Pointer = _handle.AddrOfPinnedObject();
    }

    public void Dispose()
    {
        _handle.Free();
    }
}

/// <summary>Pins a byte buffer for the duration of a native call.</summary>
internal readonly unsafe struct SlangPinnedBuffer : IDisposable
{
    private readonly GCHandle _handle;

    public SlangPinnedBuffer(byte[] buffer)
    {
        _handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
    }

    public void Dispose()
    {
        _handle.Free();
    }
}
