using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using static SlangSharp.UtilsSlangInterop;

namespace SlangSharp;

public unsafe struct SlangFileSystem : IDisposable
{
    private static readonly ISlangFileSystem.VTable* _vtable = AllocVtable();

    private ISlangFileSystem _base;

    internal GCHandle _handle;

    public SlangFileSystem(ISlangFileSystemManaged fileSystem)
    {
        _handle = GCHandle.Alloc(fileSystem);

        _base = new ISlangFileSystem
        {
            Vtbl = _vtable
        };
    }

    public void Dispose()
    {
        if (_handle.IsAllocated)
        {
            _handle.Free();
        }
    }

    private static ISlangFileSystem.VTable* AllocVtable()
    {
        ISlangFileSystem.VTable* vtable = Alloc<ISlangFileSystem.VTable>(1);
        *vtable = new ISlangFileSystem.VTable
        {
            QueryInterface = &ImplQueryInterface,
            AddRef = &ImplAddRef,
            Release = &ImplRelease,
            CastAs = &ImplCastAs,
            LoadFile = &ImplLoadFile
        };
        return vtable;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    public static SlangResult ImplQueryInterface(ISlangFileSystem* pThis, SlangUUID* riid, void** ppvObject)
    {
        return SlangResult.Ok;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    public static uint ImplAddRef(ISlangFileSystem* pThis)
    {
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    public static uint ImplRelease(ISlangFileSystem* pThis)
    {
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    public static void* ImplCastAs(ISlangFileSystem* pThis, SlangUUID* guid)
    {
        SlangUUID uuid = *guid;
        if(uuid == ISlangUnknown.UUID
        || uuid == ISlangCastable.UUID
        || uuid == ISlangFileSystem.UUID)
        {
            return pThis;
        }
        
        return null;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    public static SlangResult ImplLoadFile(ISlangFileSystem* pThis, byte* path, ISlangBlob** outBlob)
    {
        SlangFileSystem* p = (SlangFileSystem*)pThis;
        ISlangFileSystemManaged fileSystem = (ISlangFileSystemManaged)p->_handle.Target!;
        string filename = GetStringUtf8(path);
        if (fileSystem.TryLoadFile(filename, out byte[] data))
        {
            SlangBlob* blob = Alloc<SlangBlob>();
            // initial ref count is 0 the C# side will not handle the blob, so it will dispose when slang library finish using it
            *blob = new SlangBlob(data, 1);
            *outBlob = (ISlangBlob*)blob;
            return SlangResult.Ok;
        }
        else
        {
            *outBlob = null;
            return SlangResult.Failed;
        }
    }
}