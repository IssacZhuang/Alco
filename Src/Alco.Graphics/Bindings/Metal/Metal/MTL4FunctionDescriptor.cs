
namespace Metal
{
    public struct MTL4FunctionDescriptor : IDisposable
    {
        public IntPtr NativePtr;
        public static implicit operator IntPtr(MTL4FunctionDescriptor obj) => obj.NativePtr;
        public MTL4FunctionDescriptor(IntPtr ptr) => NativePtr = ptr;

        public MTL4FunctionDescriptor()
        {
            var cls = new ObjectiveCClass("MTL4FunctionDescriptor");
            NativePtr = cls.AllocInit();
        }

        public void Dispose()
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_release);
        }

        private static readonly Selector sel_release = "release";
    }
}
