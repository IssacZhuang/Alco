// Minimal DXGI and Win32 presentation interop.

using System.Runtime.InteropServices;

namespace D3D12;

internal static unsafe partial class D3D12Native
{
    internal const uint SwapchainAllowTearing = 0x800;
    internal const uint SwapchainPresentAllowTearing = 0x200;
    internal const uint SwapchainNoAltEnter = 2;
    internal const int SwapchainDeviceRemoved = unchecked((int)0x887A0005);
    internal const int SwapchainDeviceHung = unchecked((int)0x887A0006);
    internal const int SwapchainDeviceReset = unchecked((int)0x887A0007);
    internal const int SwapchainAccessLost = unchecked((int)0x887A0026);
    internal const int SwapchainInvalidCall = unchecked((int)0x887A0001);
    internal static readonly Guid SwapchainInterface = new("94d99bdb-f1f8-4ab0-b236-7da0170edab1");
    internal static readonly Guid SwapchainFactoryInterface = new("7632e1f5-ee65-4dca-87fd-84cd75f8838d");
    private static readonly Guid SwapchainResourceInterface = new("696442be-a72e-4059-bc79-5b5c98040fad");
    private static readonly Guid SwapchainHeapInterface = new("8efb471d-616c-4f49-90f7-127bb763fa51");
    private static readonly Guid SwapchainAllocatorInterface = new("6102dee4-af59-4b09-b999-b44d73f09b24");
    private static readonly Guid SwapchainCommandsInterface = new("dd171223-8b61-4769-90e3-160ccde4e2c1");

    [StructLayout(LayoutKind.Sequential)]
    internal struct SwapchainDesc1
    {
        internal uint Width;
        internal uint Height;
        internal uint Format;
        internal int Stereo;
        internal uint SampleCount;
        internal uint SampleQuality;
        internal uint BufferUsage;
        internal uint BufferCount;
        internal uint Scaling;
        internal uint SwapEffect;
        internal uint AlphaMode;
        internal uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SwapchainHeapDesc
    {
        internal uint Type;
        internal uint Count;
        internal uint Flags;
        internal uint NodeMask;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SwapchainRtvDesc
    {
        internal uint Format;
        internal uint Dimension;
        internal uint MipSlice;
        internal uint PlaneSlice;
        internal ulong Reserved;
    }

    internal static int SwapchainQueryInterface(nint instance, Guid iid, out nint result)
    {
        nint value = 0;
        int status = ((delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)(*(nint**)instance)[0])(instance, &iid, &value);
        result = value;
        return status;
    }

    internal static int SwapchainCheckTearing(nint factory, out bool supported)
    {
        int value = 0;
        int status = ((delegate* unmanaged[Stdcall]<nint, uint, void*, uint, int>)(*(nint**)factory)[28])(factory, 0, &value, sizeof(int));
        supported = value != 0;
        return status;
    }

    internal static int SwapchainMakeWindowAssociation(nint factory, nint hwnd, uint flags) =>
        ((delegate* unmanaged[Stdcall]<nint, nint, uint, int>)(*(nint**)factory)[8])(factory, hwnd, flags);

    internal static int SwapchainCreateForHwnd(nint factory, nint queue, nint hwnd, in SwapchainDesc1 desc, out nint result)
    {
        nint value = 0;
        fixed (SwapchainDesc1* pointer = &desc)
        {
            int status = ((delegate* unmanaged[Stdcall]<nint, nint, nint, SwapchainDesc1*, void*, nint, nint*, int>)(*(nint**)factory)[15])
                (factory, queue, hwnd, pointer, null, 0, &value);
            result = value;
            return status;
        }
    }

    internal static int SwapchainGetBuffer(nint swapchain, uint index, out nint result)
    {
        nint value = 0;
        Guid iid = SwapchainResourceInterface;
        int status = ((delegate* unmanaged[Stdcall]<nint, uint, Guid*, nint*, int>)(*(nint**)swapchain)[9])(swapchain, index, &iid, &value);
        result = value;
        return status;
    }

    internal static uint SwapchainGetCurrentBackBufferIndex(nint swapchain) =>
        ((delegate* unmanaged[Stdcall]<nint, uint>)(*(nint**)swapchain)[36])(swapchain);

    internal static int SwapchainResizeBuffers(nint swapchain, uint count, uint width, uint height, uint format, uint flags) =>
        ((delegate* unmanaged[Stdcall]<nint, uint, uint, uint, uint, uint, int>)(*(nint**)swapchain)[13])
            (swapchain, count, width, height, format, flags);

    internal static int SwapchainPresent(nint swapchain, uint interval, uint flags) =>
        ((delegate* unmanaged[Stdcall]<nint, uint, uint, int>)(*(nint**)swapchain)[8])(swapchain, interval, flags);

    internal static int SwapchainCheckColorSpaceSupport(nint swapchain, uint colorSpace, out uint support)
    {
        uint value = 0;
        int status = ((delegate* unmanaged[Stdcall]<nint, uint, uint*, int>)(*(nint**)swapchain)[37])(swapchain, colorSpace, &value);
        support = value;
        return status;
    }

    internal static int SwapchainSetColorSpace(nint swapchain, uint colorSpace) =>
        ((delegate* unmanaged[Stdcall]<nint, uint, int>)(*(nint**)swapchain)[38])(swapchain, colorSpace);

    internal static int SwapchainCreateRenderHeap(nint device, uint count, out nint result)
    {
        SwapchainHeapDesc desc = new() { Type = 2, Count = count };
        Guid iid = SwapchainHeapInterface;
        nint value = 0;
        int status = ((delegate* unmanaged[Stdcall]<nint, SwapchainHeapDesc*, Guid*, nint*, int>)(*(nint**)device)[14])
            (device, &desc, &iid, &value);
        result = value;
        return status;
    }

    internal static nuint SwapchainRenderHeapStart(nint heap)
    {
        nuint result = 0;
        ((delegate* unmanaged[Stdcall]<nint, nuint*, nuint*>)(*(nint**)heap)[9])(heap, &result);
        return result;
    }

    internal static void SwapchainCreateRenderView(nint device, nint resource, uint format, nuint handle)
    {
        SwapchainRtvDesc desc = new() { Format = format, Dimension = 4 };
        ((delegate* unmanaged[Stdcall]<nint, nint, SwapchainRtvDesc*, nuint, void>)(*(nint**)device)[20])(device, resource, &desc, handle);
    }

    internal static int SwapchainCreateAllocator(nint device, out nint result)
    {
        nint value = 0;
        Guid iid = SwapchainAllocatorInterface;
        int status = ((delegate* unmanaged[Stdcall]<nint, uint, Guid*, nint*, int>)(*(nint**)device)[9])(device, 0, &iid, &value);
        result = value;
        return status;
    }

    internal static int SwapchainCreateCommands(nint device, nint allocator, out nint result)
    {
        nint value = 0;
        Guid iid = SwapchainCommandsInterface;
        int status = ((delegate* unmanaged[Stdcall]<nint, uint, uint, nint, nint, Guid*, nint*, int>)(*(nint**)device)[12])
            (device, 0, 0, allocator, 0, &iid, &value);
        result = value;
        return status;
    }

    internal static int SwapchainCloseCommands(nint commands) =>
        ((delegate* unmanaged[Stdcall]<nint, int>)(*(nint**)commands)[9])(commands);

    internal static void SwapchainExecuteTail(nint queue, nint commands) =>
        ((delegate* unmanaged[Stdcall]<nint, uint, nint*, void>)(*(nint**)queue)[10])(queue, 1, &commands);

    internal static ulong SwapchainCompletedValue(nint fence) =>
        ((delegate* unmanaged[Stdcall]<nint, ulong>)(*(nint**)fence)[8])(fence);

    internal static int SwapchainSetCompletionEvent(nint fence, ulong value, nint handle) =>
        ((delegate* unmanaged[Stdcall]<nint, ulong, nint, int>)(*(nint**)fence)[9])(fence, value, handle);

    [DllImport("kernel32.dll", EntryPoint = "CreateEventW", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern nint SwapchainCreateEvent(void* attributes, int manualReset, int initialState, void* name);

    [DllImport("kernel32.dll", EntryPoint = "WaitForSingleObject", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern uint SwapchainWait(nint handle, uint milliseconds);

    [DllImport("kernel32.dll", EntryPoint = "CloseHandle")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int SwapchainCloseHandle(nint handle);

    [DllImport("kernel32.dll", EntryPoint = "GetCurrentThreadId")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern uint SwapchainCurrentThreadId();

    [DllImport("user32.dll", EntryPoint = "GetWindowThreadProcessId")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern uint SwapchainWindowThreadId(nint hwnd, uint* processId);

    [DllImport("user32.dll", EntryPoint = "IsWindow")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int SwapchainIsWindow(nint hwnd);
}
