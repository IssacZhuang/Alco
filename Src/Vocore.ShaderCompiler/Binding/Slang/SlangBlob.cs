using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using static SlangSharp.UtilsSlangInterop;

namespace SlangSharp;

public readonly unsafe struct SlangBlob : IDisposable
{
    private static readonly ISlangBlob.VTable _vtable = new()
    {
        QueryInterface = &ImplQueryInterface,
        AddRef = &ImplAddRef,
        Release = &ImplRelease,
        GetBufferPointer = &ImplGetBufferPointer,
        GetBufferSize = &ImplGetBufferSize,
    };
    private readonly ISlangBlob _base;

    private readonly void* _data;
    private readonly nuint _size;

    public SlangBlob(Span<byte> data)
    {
        fixed (byte* ptr = data)
        {
            _data = Alloc<byte>(data.Length);
            _size = (nuint)data.Length;
            Copy(ptr, _data, _size, _size);
        }
    }

    public void Dispose()
    {
        Free(_data);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    public static SlangResult ImplQueryInterface(ISlangBlob* pThis, SlangUUID* riid, void** ppvObject)
    {
        return SlangResult.Ok;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    public static uint ImplAddRef(ISlangBlob* pThis)
    {
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    public static uint ImplRelease(ISlangBlob* pThis)
    {
        return 0;
    }
    

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static void* ImplGetBufferPointer(ISlangBlob* pThis)
    {
        return ((SlangBlob*)pThis)->_data;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static nuint ImplGetBufferSize(ISlangBlob* pThis)
    {
        return ((SlangBlob*)pThis)->_size;
    }
}