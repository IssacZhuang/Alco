using System;
using Alco.ImGUI;

// Headless smoke test confirming the Alco.ImGUI native binding (cimgui.dll) exposes
// the `igGetIO` entry point. Prior to re-vendoring cimgui to the ImGui 1.91.6 docking
// commit, ImGui.GetIO() threw System.EntryPointNotFoundException because the vendored
// cimgui build had renamed the export. This program exits 0 on success and non-zero
// on any native binding failure.

try
{
    IntPtr ctx = ImGui.CreateContext();
    try
    {
        unsafe
        {
            ImGuiIOPtr io = ImGui.GetIO();
            if ((IntPtr)io.NativePtr == IntPtr.Zero)
            {
                Console.Error.WriteLine("FAIL: ImGui.GetIO() returned null pointer.");
                return 1;
            }
        }

        Console.WriteLine("PASS: ImGui.CreateContext / GetIO / DestroyContext succeeded.");
        return 0;
    }
    finally
    {
        ImGui.DestroyContext(ctx);
    }
}
catch (EntryPointNotFoundException ex)
{
    Console.Error.WriteLine($"FAIL: EntryPointNotFoundException: {ex.Message}");
    return 2;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"FAIL: Unexpected exception: {ex.GetType().Name}: {ex.Message}");
    return 3;
}
