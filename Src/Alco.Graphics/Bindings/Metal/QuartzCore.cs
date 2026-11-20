// Minimal QuartzCore CAMetalLayer and drawable bindings.

namespace Metal
{
    internal struct MTLDrawable : IDisposable
    {
        public IntPtr NativePtr;

        public static implicit operator IntPtr(MTLDrawable obj) => obj.NativePtr;

        public MTLDrawable(IntPtr ptr) => NativePtr = ptr;

        public void Dispose()
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_release);
        }

        private static readonly Selector sel_release = "release";
    }

    internal struct CAMetalDrawable : IDisposable
    {
        public IntPtr NativePtr;

        public static implicit operator MTLDrawable(CAMetalDrawable obj) => new(obj.NativePtr);

        public CAMetalDrawable(IntPtr ptr) => NativePtr = ptr;

        public void Dispose()
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_release);
        }

        public MTLTexture Texture => new(ObjectiveCRuntime.IntPtr_objc_msgSend(NativePtr, sel_texture));

        public void Present()
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_present);
        }

        private static readonly Selector sel_present = "present";
        private static readonly Selector sel_release = "release";
        private static readonly Selector sel_texture = "texture";
    }

    internal struct CAMetalLayer : IDisposable
    {
        public IntPtr NativePtr;

        public CAMetalLayer(IntPtr ptr) => NativePtr = ptr;

        public void Dispose()
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_release);
        }

        public MTLDevice Device
        {
            set => ObjectiveCRuntime.objc_msgSend(NativePtr, sel_setDevice, value);
        }

        public bool DisplaySyncEnabled
        {
            set => ObjectiveCRuntime.objc_msgSend(NativePtr, sel_setDisplaySyncEnabled, value);
        }

        public bool FramebufferOnly
        {
            set => ObjectiveCRuntime.objc_msgSend(NativePtr, sel_setFramebufferOnly, value);
        }

        public ulong MaximumDrawableCount
        {
            set => ObjectiveCRuntime.objc_msgSend(NativePtr, sel_setMaximumDrawableCount, value);
        }

        public CAMetalDrawable NextDrawable => new(ObjectiveCRuntime.IntPtr_objc_msgSend(NativePtr, sel_nextDrawable));

        public MTLPixelFormat PixelFormat
        {
            set => ObjectiveCRuntime.objc_msgSend(NativePtr, sel_setPixelFormat, (ulong)value);
        }

        public MTLResidencySet ResidencySet => new(ObjectiveCRuntime.IntPtr_objc_msgSend(NativePtr, sel_residencySet));

        private static readonly Selector sel_nextDrawable = "nextDrawable";
        private static readonly Selector sel_release = "release";
        private static readonly Selector sel_residencySet = "residencySet";
        private static readonly Selector sel_setDevice = "setDevice:";
        private static readonly Selector sel_setDisplaySyncEnabled = "setDisplaySyncEnabled:";
        private static readonly Selector sel_setFramebufferOnly = "setFramebufferOnly:";
        private static readonly Selector sel_setMaximumDrawableCount = "setMaximumDrawableCount:";
        private static readonly Selector sel_setPixelFormat = "setPixelFormat:";
    }
}
