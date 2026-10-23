using static Alco.Engine.MacOS.ObjectiveCRuntime;

namespace Alco.Engine.MacOS;

/// <summary>
/// Lightweight wrapper over an Objective-C NSView handle.
/// </summary>
public unsafe readonly struct NSView
{
    internal static readonly Selector setWantsLayer = "setWantsLayer:";
    internal static readonly Selector setLayer = "setLayer:";

    public readonly IntPtr NativePtr;
    /// <summary>
    /// Converts the wrapper to its native handle.
    /// </summary>
    /// <param name="nsView">The view to convert.</param>
    public static implicit operator IntPtr(NSView nsView) => nsView.NativePtr;

    /// <summary>
    /// Initializes a new instance of <see cref="NSView"/> from a native handle.
    /// </summary>
    /// <param name="ptr">The native view handle.</param>
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

    /// <summary>
    /// Creates and attaches a CAMetalLayer to the given view.
    /// </summary>
    /// <param name="viewHandle">The native view handle.</param>
    /// <returns>The configured metal layer.</returns>
    public static CAMetalLayer InitializeCAMetalLayer(IntPtr viewHandle)
    {
        var layer = CAMetalLayer.New();
        NSView view = new(viewHandle);
        view.wantsLayer = true;
        view.layer = layer.Handle;
        return layer;
    }
}