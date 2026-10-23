using static Alco.Engine.MacOS.ObjectiveCRuntime;

namespace Alco.Engine.MacOS;

/// <summary>
/// Lightweight wrapper over an Objective-C CAMetalLayer handle.
/// </summary>
public unsafe readonly struct CAMetalLayer
{
    public readonly IntPtr Handle;

    /// <summary>
    /// Initializes a new instance of <see cref="CAMetalLayer"/> from a native handle.
    /// </summary>
    /// <param name="ptr">The native layer handle.</param>
    public CAMetalLayer(IntPtr ptr) => Handle = ptr;

    /// <summary>
    /// Allocates and initializes a new metal layer.
    /// </summary>
    /// <returns>The new metal layer.</returns>
    public static CAMetalLayer New() => s_class.AllocInit<CAMetalLayer>();

    private static readonly ObjCClass s_class = new(nameof(CAMetalLayer));
}