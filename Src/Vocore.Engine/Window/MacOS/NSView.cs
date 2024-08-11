using static Vocore.Engine.ObjectiveCRuntime;

namespace Vocore.Engine;

internal unsafe readonly struct NSView
{
    internal static readonly Selector setWantsLayer = "setWantsLayer:";
    internal static readonly Selector setLayer = "setLayer:";

    public readonly IntPtr NativePtr;
    public static implicit operator IntPtr(NSView nsView) => nsView.NativePtr;

    public NSView(IntPtr ptr) => NativePtr = ptr;

    public Bool8 wantsLayer
    {
        get => bool8_objc_msgSend(NativePtr, "wantsLayer");
        set => objc_msgSend(NativePtr, setWantsLayer, value);
    }

    public IntPtr layer
    {
        get => IntPtr_objc_msgSend(NativePtr, "layer");
        set => objc_msgSend(NativePtr, setLayer, value);
    }
}