using static Vocore.Engine.ObjectiveCRuntime;

namespace Vocore.Engine;


internal unsafe readonly struct NSWindow
{
    public readonly IntPtr NativePtr;
    public NSWindow(IntPtr ptr)
    {
        NativePtr = ptr;
    }

    public ref NSView contentView => ref objc_msgSend<NSView>(NativePtr, "contentView");
}
