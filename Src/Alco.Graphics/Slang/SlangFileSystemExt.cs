// Internal managed Slang file-system callbacks, virtual-path normalization, and hybrid disk/embedded resolution.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Alco.Graphics.Slang;

internal delegate string? SlangFileResolver(string path);
internal delegate bool SlangPathExists(string path);

internal enum SlangPathKind : byte
{
    None,
    File,
    Directory,
}

internal delegate byte[]? SlangByteLoader(string path);
internal delegate SlangPathKind SlangPathProbe(string path);
internal delegate string SlangPathCanonicalizer(string path);

internal sealed unsafe class SlangFileSystemExt : IDisposable
{
    private static readonly Guid IidSlangUnknown = new(0x00000000, 0x0000, 0x0000, 0xC0, 0, 0, 0, 0, 0, 0, 0x46);
    private static readonly Guid IidSlangCastable = new(0x87EDE0E1, 0x4852, 0x44B0, 0x8B, 0xF2, 0xCB, 0x31, 0x87, 0x4D, 0xE2, 0x39);
    private static readonly Guid IidSlangFileSystem = new(0x003A09FC, 0x3A4D, 0x4BA0, 0xAD, 0x60, 0x1F, 0xD8, 0x63, 0xA9, 0x15, 0xAB);
    private static readonly Guid IidSlangFileSystemExt = new(0x5FB632D2, 0x979D, 0x4481, 0x9F, 0xEE, 0x66, 0x3C, 0x3F, 0x14, 0x49, 0xE1);

    private static IntPtr* s_vtable;

    private readonly SlangByteLoader _loader;
    private readonly SlangPathProbe _probe;
    private readonly SlangPathCanonicalizer _canonicalizer;
    private GCHandle _self;
    private IntPtr _comObject;

    internal SlangFileSystemExt(SlangFileResolver resolver, SlangPathExists? exists = null)
        : this(
            (string path) =>
            {
                string? content = resolver(path);
                return content == null ? null : System.Text.Encoding.UTF8.GetBytes(content);
            },
            (string path) => (exists != null && exists(path)) || resolver(path) != null ? SlangPathKind.File : SlangPathKind.None,
            NormalizePath)
    {
    }

    internal SlangFileSystemExt(SlangByteLoader loader, SlangPathProbe probe, SlangPathCanonicalizer canonicalizer)
    {
        _loader = loader;
        _probe = probe;
        _canonicalizer = canonicalizer;
        _self = GCHandle.Alloc(this);
        IntPtr* comObject = (IntPtr*)NativeMemory.Alloc(2, (nuint)IntPtr.Size);
        comObject[0] = (IntPtr)GetOrCreateVtable();
        comObject[1] = GCHandle.ToIntPtr(_self);
        _comObject = (IntPtr)comObject;
    }

    internal IntPtr Pointer => _comObject;

    public void Dispose()
    {
        if (_comObject != IntPtr.Zero)
        {
            NativeMemory.Free((void*)_comObject);
            _comObject = IntPtr.Zero;
        }
        if (_self.IsAllocated)
            _self.Free();
    }

    private static IntPtr* GetOrCreateVtable()
    {
        if (s_vtable != null)
            return s_vtable;
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
            return 0;
        }
        *outObject = IntPtr.Zero;
        return unchecked((int)0x80004002);
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
                return unchecked((int)0x80070057);
            SlangFileSystemExt fs = FromThisPtr(thisPtr);
            byte[]? bytes = fs._loader(SlangNative.StringFromPtr(path) ?? string.Empty);
            if (bytes == null)
                return unchecked((int)0x82000005); // SLANG_E_NOT_FOUND
            return TryCreateBlob(bytes, outBlob);
        }
        catch
        {
            return unchecked((int)0x80004005);
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
            if (fs._probe(pathText) == SlangPathKind.None)
                return unchecked((int)0x82000005);
            return TryCreateUtf8Blob(fs._canonicalizer(pathText), outUniqueIdentity);
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
            if (IsRooted(to))
                return TryCreateUtf8Blob(NormalizePath(to), pathOut);
            string normalizedFrom = NormalizePath(from);
            string directory = fromPathType == SlangNative.SLANG_PATH_TYPE_DIRECTORY
                ? normalizedFrom
                : GetDirectoryName(normalizedFrom);
            string combined = string.IsNullOrEmpty(directory) ? to : $"{directory.TrimEnd('/')}/{to}";
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
            SlangPathKind kind = fs._probe(pathText);
            if (kind == SlangPathKind.None)
                return unchecked((int)0x82000005);
            *pathTypeOut = kind == SlangPathKind.Directory ? SlangNative.SLANG_PATH_TYPE_DIRECTORY : SlangNative.SLANG_PATH_TYPE_FILE;
            return 0;
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
            string p = SlangNative.StringFromPtr(path)!;
            if (kind is 0 or 1 or 2)
                return TryCreateUtf8Blob(NormalizePath(p), pathOut);
            return unchecked((int)0x80004001);
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
        => unchecked((int)0x80004001);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static byte FileSystem_GetOSPathKind(IntPtr thisPtr) => 0;

    private static int TryCreateUtf8Blob(string text, IntPtr* outBlob) =>
        TryCreateBlob(System.Text.Encoding.UTF8.GetBytes(text), outBlob);

    private static int TryCreateBlob(byte[] bytes, IntPtr* outBlob)
    {
        IntPtr blob = SlangBlob.Create(bytes);
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

    private static bool IsRooted(string path) =>
        path.StartsWith('/') || path.StartsWith('\\') || (path.Length >= 2 && path[1] == ':');

    private static string GetDirectoryName(string path)
    {
        int index = path.LastIndexOf('/');
        if (index < 0)
            return string.Empty;
        if (index == 0)
            return "/";
        if (index == 2 && path.Length >= 3 && path[1] == ':')
            return path[..3];
        return path[..index];
    }

    internal static string NormalizePath(string path) => SlangPathUtility.NormalizePath(path);
}

internal static class SlangPathUtility
{
    internal static string NormalizePath(string path)
    {
        string slash = path.Replace('\\', '/');
        string prefix = string.Empty;
        string remainder = slash;
        if (slash.Length >= 2 && char.IsLetter(slash[0]) && slash[1] == ':')
        {
            bool hasRootSlash = slash.Length >= 3 && slash[2] == '/';
            prefix = hasRootSlash ? slash[..3] : slash[..2] + "/";
            remainder = hasRootSlash ? slash[3..] : slash[2..];
        }
        else if (slash.StartsWith("//", StringComparison.Ordinal))
        {
            prefix = "//";
            remainder = slash[2..];
        }
        else if (slash.StartsWith('/'))
        {
            prefix = "/";
            remainder = slash[1..];
        }

        bool rooted = prefix.Length != 0;
        List<string> segments = [];
        foreach (string segment in remainder.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (segment == ".")
                continue;
            if (segment == "..")
            {
                if (segments.Count > 0 && segments[^1] != "..")
                    segments.RemoveAt(segments.Count - 1);
                else if (!rooted)
                    segments.Add(segment);
                continue;
            }
            segments.Add(segment);
        }
        string combined = string.Join("/", segments);
        return prefix.Length != 0 ? prefix + combined : combined;
    }
}
