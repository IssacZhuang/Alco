using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using static SlangSharp.UtilsSlangInterop;

namespace SlangSharp;

public unsafe struct SlangFileSystem : IDisposable
{
    private static readonly ISlangFileSystem.VTable* _vtable = AllocVtable();

    private ISlangFileSystem _base;

    public SlangFileSystem()
    {
        _base = new ISlangFileSystem
        {
            Vtbl = _vtable
        };
    }

    public void Dispose()
    {
        
    }

    private static ISlangFileSystem.VTable* AllocVtable()
    {
        ISlangFileSystem.VTable* vtable = Alloc<ISlangFileSystem.VTable>(1);
        *vtable = new ISlangFileSystem.VTable
        {
            QueryInterface = &ImplQueryInterface,
            AddRef = &ImplAddRef,
            Release = &ImplRelease,
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
    public static SlangResult ImplLoadFile(ISlangFileSystem* pThis, byte* path, ISlangBlob** outBlob)
    {
        //Todo: Implement LoadFile
        *outBlob = null;
        return SlangResult.Ok;
    }
}