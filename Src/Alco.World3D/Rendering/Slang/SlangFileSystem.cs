using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Alco.World3D;

/// <summary>
/// A function that serves the UTF-8 text of a Slang module/import/include path,
/// or returns null when the path is unknown (Slang then reports it as missing).
/// </summary>
internal delegate string? SlangFileResolver(string path);

/// <summary>
/// A managed ISlangFileSystem COM callback serving Slang's module loads
/// (#include and import) from the game's asset system, following the same
/// hand-built-vtable pattern as the engine's DXC include handler. Slang keeps
/// the pointer for the lifetime of the compile request, so the instance must
/// outlive it.
/// </summary>
internal sealed unsafe class SlangFileSystem : IDisposable
{
    // ISlangFileSystem vtable: queryInterface, addRef, release, castAs(ISlangCastable), loadFile.
    private static IntPtr* s_vtable;

    private static readonly Guid IidSlangUnknown = new(0x00000000, 0x0000, 0x0000, 0xC0, 0, 0, 0, 0, 0, 0, 0x46);
    private static readonly Guid IidSlangCastable = new(0x87EDE0E1, 0x4852, 0x44B0, 0x8B, 0xF2, 0xCB, 0x31, 0x87, 0x4D, 0xE2, 0x39);
    private static readonly Guid IidSlangFileSystem = new(0x003A09FC, 0x3A4D, 0x4BA0, 0xAD, 0x60, 0x1F, 0xD8, 0x63, 0xA9, 0x15, 0xAB);

    private readonly SlangFileResolver _resolver;
    private GCHandle _self;
    private IntPtr _comObject;

    /// <summary>Create a file system callback backed by a resolver delegate.</summary>
    /// <param name="resolver">Serves path contents, or null when unknown.</param>
    public SlangFileSystem(SlangFileResolver resolver)
    {
        _resolver = resolver;
        _self = GCHandle.Alloc(this);
        IntPtr* comObject = (IntPtr*)NativeMemory.Alloc(2, (nuint)IntPtr.Size);
        comObject[0] = (IntPtr)GetOrCreateVtable();
        comObject[1] = GCHandle.ToIntPtr(_self);
        _comObject = (IntPtr)comObject;
    }

    /// <summary>The native ISlangFileSystem pointer to pass to Slang.</summary>
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
        IntPtr* vtable = (IntPtr*)NativeMemory.Alloc(5, (nuint)IntPtr.Size);
        vtable[0] = (IntPtr)(delegate* unmanaged[Stdcall]<IntPtr, Guid*, IntPtr*, int>)&FileSystem_QueryInterface;
        vtable[1] = (IntPtr)(delegate* unmanaged[Stdcall]<IntPtr, uint>)&FileSystem_AddRef;
        vtable[2] = (IntPtr)(delegate* unmanaged[Stdcall]<IntPtr, uint>)&FileSystem_Release;
        vtable[3] = (IntPtr)(delegate* unmanaged[Stdcall]<IntPtr, Guid*, IntPtr>)&FileSystem_CastAs;
        vtable[4] = (IntPtr)(delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr*, int>)&FileSystem_LoadFile;
        s_vtable = vtable;
        return vtable;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static unsafe int FileSystem_QueryInterface(IntPtr thisPtr, Guid* guid, IntPtr* outObject)
    {
        // Answer only the interfaces this object actually implements: blindly
        // returning "this" would let Slang call vtable slots that don't exist
        // (e.g. ISlangFileSystemExt's canonical-path methods beyond loadFile).
        if (guid != null && (*guid == IidSlangUnknown || *guid == IidSlangCastable || *guid == IidSlangFileSystem))
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
    private static IntPtr FileSystem_CastAs(IntPtr thisPtr, Guid* guid) => IntPtr.Zero;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static unsafe int FileSystem_LoadFile(IntPtr thisPtr, IntPtr path, IntPtr* outBlob)
    {
        try
        {
            *outBlob = IntPtr.Zero;
            if (path == IntPtr.Zero)
            {
                return unchecked((int)0x80070057); // E_INVALIDARG
            }

            SlangFileSystem fileSystem = FromThisPtr(thisPtr);
            string? content = fileSystem._resolver(SlangNative.StringFromPtr(path) ?? string.Empty);
            if (content == null)
            {
                return unchecked((int)0x80070002); // file not found
            }

            // slang_createBlob copies the data into a ref-counted ISlangBlob
            // owned by the compiler from here on.
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(content);
            IntPtr blob = SlangNative.slang_createBlob(bytes, (nuint)bytes.Length);
            if (blob == IntPtr.Zero)
            {
                return unchecked((int)0x80004005); // E_FAIL
            }
            *outBlob = blob;
            return 0; // S_OK
        }
        catch
        {
            return unchecked((int)0x80004005); // E_FAIL
        }
    }

    private static SlangFileSystem FromThisPtr(IntPtr thisPtr)
    {
        IntPtr* comObject = (IntPtr*)thisPtr;
        return (SlangFileSystem)GCHandle.FromIntPtr(comObject[1]).Target!;
    }
}
