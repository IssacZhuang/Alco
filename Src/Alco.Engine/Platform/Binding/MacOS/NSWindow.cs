using static Alco.Engine.MacOS.ObjectiveCRuntime;

namespace Alco.Engine.MacOS;


public unsafe readonly struct NSWindow
{
    public readonly IntPtr NativePtr;
    public NSWindow(IntPtr ptr)
    {
        NativePtr = ptr;
    }

    public ref NSView contentView => ref objc_msgSend<NSView>(NativePtr, "contentView");

    public static CAMetalLayer InitializeCAMetalLayer(IntPtr windowHandle)
    {
        NSWindow window = new(windowHandle);
        return NSView.InitializeCAMetalLayer(window.contentView.NativePtr);
    }
}
