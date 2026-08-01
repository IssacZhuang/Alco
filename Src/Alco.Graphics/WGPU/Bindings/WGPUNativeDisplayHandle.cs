using System.Runtime.InteropServices;

namespace WebGPU;

/// <summary>
/// Hand-written binding for the tagged union WGPUNativeDisplayHandle (wgpu.h),
/// which the generator cannot emit because of its anonymous union member.
/// Only used by the GLES backend on Wayland/X11; zero-initialization yields
/// WGPUNativeDisplayHandleType_None on other platforms.
/// </summary>
internal partial struct WGPUNativeDisplayHandle
{
    public WGPUNativeDisplayHandleType type;
    public WGPUNativeDisplayHandleData data;
}

[StructLayout(LayoutKind.Explicit)]
internal partial struct WGPUNativeDisplayHandleData
{
    [FieldOffset(0)] public WGPUXlibDisplayHandle xlib;
    [FieldOffset(0)] public WGPUXcbDisplayHandle xcb;
    [FieldOffset(0)] public WGPUWaylandDisplayHandle wayland;
}
