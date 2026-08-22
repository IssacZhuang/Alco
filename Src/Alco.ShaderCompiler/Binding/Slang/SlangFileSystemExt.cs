using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Alco.ShaderCompiler;

// ─────────────────────────────────────────────────────────────────────────────
// A managed ISlangFileSystemExt COM object that serves slang's module loads
// (`import`, `#include`) from a managed resolver delegate, so pak files,
// embedded assets and directory watchers keep working — slang imports are
// fully virtualizable. Same hand-built-vtable pattern as the engine's DXC
// include handler. Slang holds the pointer for the session's lifetime.
//
// Vtable layout (ISlangFileSystemExt):
//   0 queryInterface      1 addRef        2 release         3 castAs
//   4 loadFile            5 getFileUniqueIdentity
//   6 calcCombinedPath    7 getPathType
//   8 getPath             9 clearCache
//  10 enumeratePathContents  11 getOSPathKind
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// A function that serves the UTF-8 text of a slang module/import/include path,
/// or returns null when the path is unknown (slang then reports it as missing).
/// </summary>
public delegate string? SlangFileResolver(string path);

/// <summary>A function that classifies a path, or returns null when unknown.</summary>
public delegate bool SlangPathExists(string path);

internal sealed unsafe class SlangFileSystemExt : IDisposable
{
    private static readonly Guid IidSlangUnknown = new(0x00000000, 0x0000, 0x0000, 0xC0, 0, 0, 0, 0, 0, 0, 0x46);
    private static readonly Guid IidSlangCastable = new(0x87EDE0E1, 0x4852, 0x44B0, 0x8B, 0xF2, 0xCB, 0x31, 0x87, 0x4D, 0xE2, 0x39);
    private static readonly Guid IidSlangFileSystem = new(0x003A09FC, 0x3A4D, 0x4BA0, 0xAD, 0x60, 0x1F, 0xD8, 0x63, 0xA9, 0x15, 0xAB);
    private static readonly Guid IidSlangFileSystemExt = new(0x5FB632D2, 0x979D, 0x4481, 0x9F, 0xEE, 0x66, 0x3C, 0x3F, 0x14, 0x49, 0xE1);

    private static IntPtr* s_vtable;

    private readonly SlangFileResolver _resolver;
    private readonly SlangPathExists? _exists;
    private GCHandle _self;
    private IntPtr _comObject;

    /// <summary>Creates a virtual file system backed by resolver delegates.</summary>
    public SlangFileSystemExt(SlangFileResolver resolver, SlangPathExists? exists = null)
    {
        _resolver = resolver;
        _exists = exists;
        _self = GCHandle.Alloc(this);
        IntPtr* comObject = (IntPtr*)NativeMemory.Alloc(2, (nuint)IntPtr.Size);
        comObject[0] = (IntPtr)GetOrCreateVtable();
        comObject[1] = GCHandle.ToIntPtr(_self);
        _comObject = (IntPtr)comObject;
    }

    /// <summary>The native ISlangFileSystem pointer to pass to slang (as ISlangFileSystem*, slang QIs for Ext).</summary>
    public IntPtr Pointer => _comObject;

    /// <inheritdoc />
    public void Dispose()
    {
        if (_comObject != IntPtr.Zero)
        {
            NativeMemory.Free((void*)_comObject);
            _comObject = IntPtr.Zero;
        }
        if (_self.IsAllocated)
        {
            _self.Free();
        }
    }

    private static IntPtr* GetOrCreateVtable()
    {
        if (s_vtable != null)
        {
            return s_vtable;
        }
        IntPtr* vtable = (IntPtr*)NativeMemory.Alloc(12, (nuint)IntPtr.Size);
        vtable[0] = (IntPtr)(delegate* unmanaged[Stdcall]<IntPtr, Guid*, IntPtr*, int>)&FileSystem_QueryInterface;
        vtable[1] = (IntPtr)(delegate* unmanaged[Stdcall]<IntPtr, uint>)&FileSystem_AddRef;
        vtable[2] = (IntPtr)(delegate* unmanaged[Stdcall]<IntPtr, uint>)&FileSystem_Release;
        vtable[3] = (IntPtr)(delegate* unmanaged[Stdcall]<IntPtr, Guid*, IntPtr>)&FileSystem_CastAs;
        vtable[4] = (IntPtr)(delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr*, int>)&FileSystem_LoadFile;
        vtable[5] = (IntPtr)(delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr*, int>)&FileSystem_GetFileUniqueIdentity;
        vtable[6] = (IntPtr)(delegate* unmanaged[Stdcall]<IntPtr, int, IntPtr, IntPtr, IntPtr*, int>)&FileSystem_CalcCombinedPath;
        vtable[7] = (IntPtr)(delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int*, int>)&FileSystem_GetPathType;
        vtable[8] = (IntPtr)(delegate* unmanaged[Stdcall]<IntPtr, int, IntPtr, IntPtr*, int>)&FileSystem_GetPath;
        vtable[9] = (IntPtr)(delegate* unmanaged[Stdcall]<IntPtr, void>)&FileSystem_ClearCache;
        vtable[10] = (IntPtr)(delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr, IntPtr, int>)&FileSystem_EnumeratePathContents;
        vtable[11] = (IntPtr)(delegate* unmanaged[Stdcall]<IntPtr, byte>)&FileSystem_GetOSPathKind;
        s_vtable = vtable;
        return vtable;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int FileSystem_QueryInterface(IntPtr thisPtr, Guid* guid, IntPtr* outObject)
    {
        if (guid != null && (*guid == IidSlangUnknown || *guid == IidSlangCastable || *guid == IidSlangFileSystem || *guid == IidSlangFileSystemExt))
        {
            *outObject = thisPtr;
            return 0; // S_OK
        }
        *outObject = IntPtr.Zero;
        return unchecked((int)0x80004002); // E_NOINTERFACE
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static uint FileSystem_AddRef(IntPtr thisPtr) => 2;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static uint FileSystem_Release(IntPtr thisPtr) => 1;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static IntPtr FileSystem_CastAs(IntPtr thisPtr, Guid* guid)
    {
        if (guid != null && (*guid == IidSlangUnknown || *guid == IidSlangCastable || *guid == IidSlangFileSystem || *guid == IidSlangFileSystemExt))
            return thisPtr;
        return IntPtr.Zero;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int FileSystem_LoadFile(IntPtr thisPtr, IntPtr path, IntPtr* outBlob)
    {
        try
        {
            *outBlob = IntPtr.Zero;
            if (path == IntPtr.Zero)
                return unchecked((int)0x80070057); // E_INVALIDARG
            SlangFileSystemExt fs = FromThisPtr(thisPtr);
            string? content = fs._resolver(SlangNative.StringFromPtr(path) ?? string.Empty);
            if (content == null)
                return unchecked((int)0x80070002); // file not found
            return TryCreateUtf8Blob(content, outBlob);
        }
        catch
        {
            return unchecked((int)0x80004005); // E_FAIL
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int FileSystem_GetFileUniqueIdentity(IntPtr thisPtr, IntPtr path, IntPtr* outUniqueIdentity)
    {
        try
        {
            *outUniqueIdentity = IntPtr.Zero;
            if (path == IntPtr.Zero)
                return unchecked((int)0x80070057);
            SlangFileSystemExt fs = FromThisPtr(thisPtr);
            string pathText = SlangNative.StringFromPtr(path)!;
            // A file has an identity iff we could load it: consult the cheap
            // Exists classifier first, then the resolver (which may map the
            // path, e.g. search-root emulation, without listing it in Exists).
            if (fs._exists != null && !fs._exists(pathText) && fs._resolver(pathText) == null)
                return unchecked((int)0x80070002); // E_NOT_FOUND-ish
            return TryCreateUtf8Blob(NormalizePath(pathText), outUniqueIdentity);
        }
        catch
        {
            return unchecked((int)0x80004005);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int FileSystem_CalcCombinedPath(IntPtr thisPtr, int fromPathType, IntPtr fromPath, IntPtr path, IntPtr* pathOut)
    {
        try
        {
            *pathOut = IntPtr.Zero;
            if (fromPath == IntPtr.Zero || path == IntPtr.Zero)
                return unchecked((int)0x80070057);
            string from = SlangNative.StringFromPtr(fromPath)!;
            string to = SlangNative.StringFromPtr(path)!;
            string directory = fromPathType == SlangNative.SLANG_PATH_TYPE_DIRECTORY
                ? from
                : GetDirectoryName(from);
            string combined = string.IsNullOrEmpty(directory) ? to : $"{directory}/{to}";
            return TryCreateUtf8Blob(NormalizePath(combined), pathOut);
        }
        catch
        {
            return unchecked((int)0x80004005);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int FileSystem_GetPathType(IntPtr thisPtr, IntPtr path, int* pathTypeOut)
    {
        try
        {
            *pathTypeOut = SlangNative.SLANG_PATH_TYPE_FILE;
            if (path == IntPtr.Zero)
                return unchecked((int)0x80070057);
            SlangFileSystemExt fs = FromThisPtr(thisPtr);
            string pathText = SlangNative.StringFromPtr(path)!;
            if (fs._resolver(pathText) != null || (fs._exists?.Invoke(pathText) ?? false))
                return 0;
            return unchecked((int)0x80070002); // not found
        }
        catch
        {
            return unchecked((int)0x80004005);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int FileSystem_GetPath(IntPtr thisPtr, int kind, IntPtr path, IntPtr* pathOut)
    {
        try
        {
            *pathOut = IntPtr.Zero;
            if (path == IntPtr.Zero)
                return unchecked((int)0x80070057);
            if (kind is 0 or 1 or 2) // Simplified, Canonical, Display
                return TryCreateUtf8Blob(NormalizePath(SlangNative.StringFromPtr(path)!), pathOut);
            return unchecked((int)0x80004001); // E_NOT_IMPLEMENTED (OperatingSystem)
        }
        catch
        {
            return unchecked((int)0x80004005);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static void FileSystem_ClearCache(IntPtr thisPtr) { }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int FileSystem_EnumeratePathContents(IntPtr thisPtr, IntPtr path, IntPtr callback, IntPtr userData)
        => unchecked((int)0x80004001); // E_NOT_IMPLEMENTED — allowed for normal operation

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static byte FileSystem_GetOSPathKind(IntPtr thisPtr)
        => 0; // None — paths do not map to the OS file system

    private static int TryCreateUtf8Blob(string text, IntPtr* outBlob)
    {
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(text);
        IntPtr blob = SlangNative.slang_createBlob(bytes, (nuint)bytes.Length);
        if (blob == IntPtr.Zero)
            return unchecked((int)0x80004005);
        *outBlob = blob;
        return 0;
    }

    private static SlangFileSystemExt FromThisPtr(IntPtr thisPtr)
    {
        IntPtr* comObject = (IntPtr*)thisPtr;
        return (SlangFileSystemExt)GCHandle.FromIntPtr(comObject[1]).Target!;
    }

    private static string GetDirectoryName(string path)
    {
        int index = path.LastIndexOf('/');
        return index <= 0 ? string.Empty : path[..index];
    }

    /// <summary>Normalizes separators to '/' and resolves '.', '..' segments lexically.</summary>
    internal static string NormalizePath(string path) => SlangPathUtility.NormalizePath(path);
}

/// <summary>Path normalization for the slang virtual file space ('/' separators, lexical '.'/'..' resolution).</summary>
public static class SlangPathUtility
{
    /// <summary>Normalizes separators to '/' and resolves '.', '..' segments lexically.</summary>
    public static string NormalizePath(string path)
    {
        bool absolute = path.StartsWith('/');
        Stack<string> segments = new();
        foreach (string segment in path.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (segment == ".")
                continue;
            if (segment == "..")
            {
                if (segments.Count > 0 && segments.Peek() != "..")
                    segments.Pop();
                else if (!absolute)
                    segments.Push(segment);
                continue;
            }
            segments.Push(segment);
        }
        string combined = string.Join("/", segments.Reverse());
        return absolute ? "/" + combined : combined;
    }
}
