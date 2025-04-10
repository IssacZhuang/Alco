using static Alco.Engine.MacOS.ObjectiveCRuntime;

namespace Alco.Engine.MacOS;

public unsafe readonly struct CAMetalLayer
{
    public readonly IntPtr Handle;

    public CAMetalLayer(IntPtr ptr) => Handle = ptr;

    public static CAMetalLayer New() => s_class.AllocInit<CAMetalLayer>();

    private static readonly ObjCClass s_class = new(nameof(CAMetalLayer));
}