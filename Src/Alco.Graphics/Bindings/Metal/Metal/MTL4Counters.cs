using System.Runtime.InteropServices;

namespace Metal
{
    public enum MTL4CounterHeapType : long
    {
        Invalid,
        Timestamp,
    }

    public enum MTL4TimestampGranularity : long
    {
        Relaxed = 0,
        Precise = 1,
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MTL4TimestampHeapEntry
    {
        public ulong timestamp;
    }

    public struct MTL4CounterHeap : IDisposable
    {
        public IntPtr NativePtr;
        public static implicit operator IntPtr(MTL4CounterHeap obj) => obj.NativePtr;
        public MTL4CounterHeap(IntPtr ptr) => NativePtr = ptr;

        public void Dispose()
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_release);
        }

        public ulong Count => ObjectiveCRuntime.ulong_objc_msgSend(NativePtr, sel_count);

        public NSString Label
        {
            get => new(ObjectiveCRuntime.IntPtr_objc_msgSend(NativePtr, sel_label));
            set => ObjectiveCRuntime.objc_msgSend(NativePtr, sel_setLabel, value);
        }

        public MTL4CounterHeapType Type => (MTL4CounterHeapType)ObjectiveCRuntime.long_objc_msgSend(NativePtr, sel_type);

        public void InvalidateCounterRange(NSRange range)
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_invalidateCounterRange, range);
        }

        public NSData ResolveCounterRange(NSRange range)
        {
            return new(ObjectiveCRuntime.IntPtr_objc_msgSend(NativePtr, sel_resolveCounterRange, range));
        }

        private static readonly Selector sel_count = "count";
        private static readonly Selector sel_invalidateCounterRange = "invalidateCounterRange:";
        private static readonly Selector sel_label = "label";
        private static readonly Selector sel_resolveCounterRange = "resolveCounterRange:";
        private static readonly Selector sel_setLabel = "setLabel:";
        private static readonly Selector sel_type = "type";
        private static readonly Selector sel_release = "release";
    }

    public struct MTL4CounterHeapDescriptor : IDisposable
    {
        public IntPtr NativePtr;
        public static implicit operator IntPtr(MTL4CounterHeapDescriptor obj) => obj.NativePtr;
        public MTL4CounterHeapDescriptor(IntPtr ptr) => NativePtr = ptr;

        public MTL4CounterHeapDescriptor()
        {
            var cls = new ObjectiveCClass("MTL4CounterHeapDescriptor");
            NativePtr = cls.AllocInit();
        }

        public void Dispose()
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_release);
        }

        public ulong Count
        {
            get => ObjectiveCRuntime.ulong_objc_msgSend(NativePtr, sel_count);
            set => ObjectiveCRuntime.objc_msgSend(NativePtr, sel_setCount, value);
        }

        public MTL4CounterHeapType Type
        {
            get => (MTL4CounterHeapType)ObjectiveCRuntime.long_objc_msgSend(NativePtr, sel_type);
            set => ObjectiveCRuntime.objc_msgSend(NativePtr, sel_setType, (long)value);
        }

        private static readonly Selector sel_count = "count";
        private static readonly Selector sel_setCount = "setCount:";
        private static readonly Selector sel_setType = "setType:";
        private static readonly Selector sel_type = "type";
        private static readonly Selector sel_release = "release";
    }
}
