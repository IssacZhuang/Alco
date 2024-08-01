using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using static SlangSharp.UtilsSlangInterop;

namespace SlangSharp;

public unsafe struct SlangBlob : IDisposable
{
    private static readonly ISlangBlob.VTable* _vtable = AllocVtable();
    private readonly ISlangBlob _base;

    private readonly void* _data;
    private readonly nuint _size;

    private uint _refCount;
    private uint _disposed;

    public SlangBlob(Span<byte> data, uint initialRefCount = 1)
    {
        fixed (byte* ptr = data)
        {
            _data = Alloc<byte>(data.Length);
            _size = (nuint)data.Length;
            Copy(ptr, _data, _size, _size);
        }

        _refCount = initialRefCount;
        _disposed = 0;

        _base = new ISlangBlob
        {
            Vtbl = _vtable
        };
    }

    public void Dispose()
    {
        if(Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            Free(_data);
        }
    }

    public static ISlangBlob.VTable* AllocVtable(){
        ISlangBlob.VTable* vtable = Alloc<ISlangBlob.VTable>(1);
        *vtable = new ISlangBlob.VTable
        {
            QueryInterface = &ImplQueryInterface,
            AddRef = &ImplAddRef,
            Release = &ImplRelease,
            GetBufferPointer = &ImplGetBufferPointer,
            GetBufferSize = &ImplGetBufferSize
        };
        return vtable;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    public static SlangResult ImplQueryInterface(ISlangBlob* pThis, SlangUUID* riid, void** ppvObject)
    {
        return SlangResult.Ok;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    public static uint ImplAddRef(ISlangBlob* pThis)
    {
        //Console.WriteLine($"AddRef: {((SlangBlob*)pThis)->_refCount + 1}");
        return Interlocked.Increment(ref ((SlangBlob*)pThis)->_refCount);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    public static uint ImplRelease(ISlangBlob* pThis)
    {
        uint refCount = Interlocked.Decrement(ref ((SlangBlob*)pThis)->_refCount);
        //Console.WriteLine($"Release: {refCount}");
        if (refCount == 0)
        {
            //Console.WriteLine("Dispose");
            ((SlangBlob*)pThis)->Dispose();
        }
        
        return refCount;
    }
    

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static void* ImplGetBufferPointer(ISlangBlob* pThis)
    {
        return ((SlangBlob*)pThis)->_data;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nuint ImplGetBufferSize(ISlangBlob* pThis)
    {
        return ((SlangBlob*)pThis)->_size;
    }
}