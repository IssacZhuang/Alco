using System;
using Vortice.Vulkan;

using static Vortice.Vulkan.Vulkan;

namespace Vocore.Graphics.Vulkan
{
    internal static partial class UtilsVulkan
    {
        public unsafe static VkSurfaceKHR CreateSurface(VkInstance instance, SurfaceSource surface)
        {

            VkSurfaceKHR result = VkSurfaceKHR.Null;
            switch (surface)
            {
                case HtmlCanvasSurfaceSource htmlCanvasSurface:
                    throw new NotSupportedException("HtmlCanvasSurfaceSource is not supported on Vulkan");

                case AndroidWindowSurfaceSource androidWindowSurface:
                    var createInfo = new VkAndroidSurfaceCreateInfoKHR
                    {
                        window = androidWindowSurface.Window
                    };

                    vkCreateAndroidSurfaceKHR(instance, &createInfo, null, &result).CheckResult("Failed to create Android surface");
                    break;

                case MetalLayerSurfaceHandle metalLayerSurface:
                    throw new NotImplementedException("MetalLayerSurfaceHandle currently not implemented");
                case Win32SurfaceSource win32Surface:
                    var win32CreateInfo = new VkWin32SurfaceCreateInfoKHR()
                    {
                        hinstance = win32Surface.HInstance,
                        hwnd = win32Surface.Hwnd
                    };

                    vkCreateWin32SurfaceKHR(instance, &win32CreateInfo, null, &result).CheckResult("Failed to create Win32 surface");
                    break;

                case WaylandSurfaceSource waylandSurface:
                    var waylandCreateInfo = new VkWaylandSurfaceCreateInfoKHR()
                    {
                        display = waylandSurface.Display,
                        surface = waylandSurface.Surface
                    };

                    vkCreateWaylandSurfaceKHR(instance, &waylandCreateInfo, null, &result).CheckResult("Failed to create Wayland surface");
                    break;

                case XcbWindowSurfaceSource xcbWindowSurface:
                    var xcbCreateInfo = new VkXcbSurfaceCreateInfoKHR()
                    {
                        connection = xcbWindowSurface.Connection,
                        window = xcbWindowSurface.Window
                    };

                    vkCreateXcbSurfaceKHR(instance, &xcbCreateInfo, null, &result).CheckResult("Failed to create Xcb surface");
                    break;

                case XlibWindowSurfaceSource xlibWindowSurface:
                    var xlibCreateInfo = new VkXlibSurfaceCreateInfoKHR()
                    {
                        display = xlibWindowSurface.Display,
                        window = (nuint)xlibWindowSurface.Window
                    };

                    vkCreateXlibSurfaceKHR(instance, &xlibCreateInfo, null, &result).CheckResult("Failed to create Xlib surface");
                    break;
            }

            return result;
        }
    }
}