using System.Runtime.InteropServices;

namespace Metal
{
    [StructLayout(LayoutKind.Sequential)]
    public struct NSFastEnumerationState
    {
        public IntPtr itemsPtr;
    }

    public struct NSEnumerator : IDisposable
    {
        public IntPtr NativePtr;
        public static implicit operator IntPtr(NSEnumerator obj) => obj.NativePtr;
        public static implicit operator NSFastEnumeration(NSEnumerator obj) => new(obj.NativePtr);
        public NSEnumerator(IntPtr ptr) => NativePtr = ptr;

        public void Dispose()
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_release);
        }

        public ulong CountByEnumerating(NSFastEnumerationState pState, NSObject pBuffer, ulong len)
        {
            return ObjectiveCRuntime.ulong_objc_msgSend(NativePtr, sel_countByEnumeratingWithStateobjectscount, pState, pBuffer, len);
        }

        private static readonly Selector sel_countByEnumeratingWithStateobjectscount = "countByEnumeratingWithState:objects:count:";
        private static readonly Selector sel_release = "release";
    }

    public struct NSFastEnumeration : IDisposable
    {
        public IntPtr NativePtr;
        public static implicit operator IntPtr(NSFastEnumeration obj) => obj.NativePtr;
        public NSFastEnumeration(IntPtr ptr) => NativePtr = ptr;

        public void Dispose()
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_release);
        }

        public ulong CountByEnumerating(NSFastEnumerationState pState, NSObject pBuffer, ulong len)
        {
            return ObjectiveCRuntime.ulong_objc_msgSend(NativePtr, sel_countByEnumeratingWithStateobjectscount, pState, pBuffer, len);
        }

        private static readonly Selector sel_countByEnumeratingWithStateobjectscount = "countByEnumeratingWithState:objects:count:";
        private static readonly Selector sel_release = "release";
    }
}
