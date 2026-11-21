using static Alco.Engine.MacOS.ObjectiveCRuntime;

namespace Alco.Engine.MacOS;


/// <summary>
/// Lightweight wrapper over an Objective-C NSWindow handle.
/// </summary>
public unsafe readonly struct NSWindow
{
    public readonly IntPtr NativePtr;
    /// <summary>
    /// Initializes a new instance of <see cref="NSWindow"/> from a native handle.
    /// </summary>
    /// <param name="ptr">The native window handle.</param>
    public NSWindow(IntPtr ptr)
    {
        NativePtr = ptr;
    }

    private static readonly Selector s_contentView = "contentView";

    /// <summary>
    /// Gets the content view of the window.
    /// </summary>
    public NSView contentView => new(IntPtr_objc_msgSend(NativePtr, s_contentView));

    /// <summary>
    /// Creates and attaches a CAMetalLayer to the window's content view.
    /// </summary>
    /// <param name="windowHandle">The native window handle.</param>
    /// <returns>The configured metal layer.</returns>
    public static CAMetalLayer InitializeCAMetalLayer(IntPtr windowHandle)
    {
        NSWindow window = new(windowHandle);
        return NSView.InitializeCAMetalLayer(window.contentView.NativePtr);
    }
}
