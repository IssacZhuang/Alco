// Copyright (c) Amer Koleci and Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repository root for more information.

using System.Drawing;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using static SDL3.SDL3;

namespace SDL3;

#region Enums
/// <summary>Boolean type used by SDL native APIs.</summary>
public enum SDL_bool : byte
{
    SDL_FALSE = SDL3.SDL_FALSE,
    SDL_TRUE = SDL3.SDL_TRUE
}

/// <summary>Flags describing a window's state and creation options.</summary>
[Flags]
public enum SDL_WindowFlags : ulong
{
    None = 0,
    /// <unmanaged>SDL_WINDOW_FULLSCREEN</unmanaged>
    Fullscreen = SDL_WINDOW_FULLSCREEN,
    /// <unmanaged>SDL_WINDOW_OPENGL</unmanaged>
    OpenGL = SDL_WINDOW_OPENGL,
    /// <unmanaged>SDL_WINDOW_OCCLUDED</unmanaged>
    Occluded = SDL_WINDOW_OCCLUDED,
    /// <unmanaged>SDL_WINDOW_HIDDEN</unmanaged>
    Hidden = SDL_WINDOW_HIDDEN,
    /// <unmanaged>SDL_WINDOW_BORDERLESS</unmanaged>
    Borderless = SDL_WINDOW_BORDERLESS,
    /// <unmanaged>SDL_WINDOW_RESIZABLE</unmanaged>
    Resizable = SDL_WINDOW_RESIZABLE,
    /// <unmanaged>SDL_WINDOW_MINIMIZED</unmanaged>
    Minimized = SDL_WINDOW_MINIMIZED,
    /// <unmanaged>SDL_WINDOW_MAXIMIZED</unmanaged>
    Maximized = SDL_WINDOW_MAXIMIZED,
    /// <unmanaged>SDL_WINDOW_MOUSE_GRABBED</unmanaged>
    MouseGrabbed = SDL_WINDOW_MOUSE_GRABBED,
    /// <unmanaged>SDL_WINDOW_INPUT_FOCUS</unmanaged>
    InputFocus = SDL_WINDOW_INPUT_FOCUS,
    /// <unmanaged>SDL_WINDOW_MOUSE_FOCUS</unmanaged>
    MouseFocus = SDL_WINDOW_MOUSE_FOCUS,
    /// <unmanaged>SDL_WINDOW_EXTERNAL</unmanaged>
    External = SDL_WINDOW_EXTERNAL,
    /// <unmanaged>SDL_WINDOW_HIGH_PIXEL_DENSITY</unmanaged>
    HighPixelDensity = SDL_WINDOW_HIGH_PIXEL_DENSITY,
    /// <unmanaged>SDL_WINDOW_MOUSE_CAPTURE</unmanaged>
    MouseCapture = SDL_WINDOW_MOUSE_CAPTURE,
    /// <unmanaged>SDL_WINDOW_ALWAYS_ON_TOP</unmanaged>
    AlwaysOnTop = SDL_WINDOW_ALWAYS_ON_TOP,
    /// <unmanaged>SDL_WINDOW_UTILITY</unmanaged>
    Utility = SDL_WINDOW_UTILITY,
    /// <unmanaged>SDL_WINDOW_TOOLTIP</unmanaged>
    Tooltip = SDL_WINDOW_TOOLTIP,
    /// <unmanaged>SDL_WINDOW_POPUP_MENU</unmanaged>
    PopupMenu = SDL_WINDOW_POPUP_MENU,
    /// <unmanaged>SDL_WINDOW_KEYBOARD_GRABBED</unmanaged>
    KeyboardGrabbed = SDL_WINDOW_KEYBOARD_GRABBED,
    /// <unmanaged>SDL_WINDOW_VULKAN</unmanaged>
    Vulkan = SDL_WINDOW_VULKAN,
    /// <unmanaged>SDL_WINDOW_METAL</unmanaged>
    Metal = SDL_WINDOW_METAL,
    /// <unmanaged>SDL_WINDOW_TRANSPARENT</unmanaged>
    Transparent = SDL_WINDOW_TRANSPARENT,
    /// <unmanaged>SDL_WINDOW_NOT_FOCUSABLE</unmanaged>
    NotFocusable = SDL_WINDOW_NOT_FOCUSABLE,
}
#endregion

/// <summary>Receives a message from SDL's logging system.</summary>
public delegate void SDL_LogOutputFunction(SDL_LogCategory category, SDL_LogPriority priority, string? message);

// https://github.com/libsdl-org/SDL/blob/main/docs/README-migration.md

/// <summary>Bindings to the SDL3 native library.</summary>
public static unsafe partial class SDL3
{
    private const DllImportSearchPath DefaultDllImportSearchPath = DllImportSearchPath.ApplicationDirectory | DllImportSearchPath.UserDirectories | DllImportSearchPath.UseDllDirectoryForDependencies;

    /// <summary>
    /// Raised whenever a native library is loaded by SDL.
    /// Handlers can be added to this event to customize how libraries are loaded, and they will be used first whenever a new native library is being resolved.
    /// </summary>
    public static event DllImportResolver? SDL_DllImporterResolver;

    private const string LibName = "SDL3";

    static SDL3()
    {
        NativeLibrary.SetDllImportResolver(Assembly.GetExecutingAssembly(), OnDllImport);
    }

    private static IntPtr OnDllImport(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (libraryName != LibName)
        {
            return IntPtr.Zero;
        }

        IntPtr nativeLibrary = IntPtr.Zero;
        DllImportResolver? resolver = SDL_DllImporterResolver;
        if (resolver != null)
        {
            nativeLibrary = resolver(libraryName, assembly, searchPath);
        }

        if (nativeLibrary != IntPtr.Zero)
        {
            return nativeLibrary;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            if (NativeLibrary.TryLoad("SDL3.dll", assembly, searchPath, out nativeLibrary))
            {
                return nativeLibrary;
            }
        }
        else
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                if (NativeLibrary.TryLoad("libSDL3.so", assembly, searchPath, out nativeLibrary))
                {
                    return nativeLibrary;
                }
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                if (NativeLibrary.TryLoad("libSDL3.dylib", assembly, searchPath, out nativeLibrary))
                {
                    return nativeLibrary;
                }

                if (NativeLibrary.TryLoad("/usr/local/opt/SDL3/lib/libSDL3.dylib", assembly, searchPath, out nativeLibrary))
                {
                    return nativeLibrary;
                }
            }
        }

        if (NativeLibrary.TryLoad("libSDL3", assembly, searchPath, out nativeLibrary))
        {
            return nativeLibrary;
        }

        if (NativeLibrary.TryLoad("SDL3", assembly, searchPath, out nativeLibrary))
        {
            return nativeLibrary;
        }

        return 0;
    }

    /// <summary>The SDL false value.</summary>
    public const SDL_bool SDL_FALSE = (SDL_bool)(0);
    /// <summary>The SDL true value.</summary>
    public const SDL_bool SDL_TRUE = (SDL_bool)(1);

    //[NativeTypeName("#define SDL_SIZE_MAX SIZE_MAX")]
    /// <summary>The maximum value of a native size type.</summary>
    public const ulong SDL_SIZE_MAX = 0xffffffffffffffffUL;

    /// <summary>Builds a four-character code from four bytes.</summary>
    /// <param name="A">The first byte.</param>
    /// <param name="B">The second byte.</param>
    /// <param name="C">The third byte.</param>
    /// <param name="D">The fourth byte.</param>
    /// <returns>The encoded four-character code.</returns>
    public static uint SDL_FOURCC(byte A, byte B, byte C, byte D)
    {
        return (uint)(A | (B << 8) | (C << 16) | (D << 24));
    }

    /// <summary>Converts seconds to nanoseconds.</summary>
    /// <param name="seconds">The duration in seconds.</param>
    /// <returns>The duration in nanoseconds.</returns>
    public static ulong SDL_SECONDS_TO_NS(ulong seconds) => seconds * SDL_NS_PER_SECOND;
    /// <summary>Converts nanoseconds to seconds.</summary>
    /// <param name="ns">The duration in nanoseconds.</param>
    /// <returns>The duration in seconds.</returns>
    public static ulong SDL_NS_TO_SECONDS(ulong ns) => ns / SDL_NS_PER_SECOND;

    /// <summary>Frees memory allocated by SDL.</summary>
    /// <param name="mem">The memory to free.</param>
    /// <returns>The result of the native call.</returns>
    [LibraryImport(LibName, EntryPoint = "SDL_free")]
    public static partial nint SDL_free(void* mem);

    #region SDL_hints.h
    /// <summary>Sets an SDL hint from a UTF-8 name and a boolean value.</summary>
    /// <param name="name">The null-terminated hint name.</param>
    /// <param name="value">The boolean value to store.</param>
    /// <returns>True on success; otherwise false.</returns>
    public static SDLBool SDL_SetHint(ReadOnlySpan<byte> name, bool value)
    {
        fixed (byte* pName = name)
        fixed (byte* pValue = (value ? "1"u8 : "0"u8))
            return SDL_SetHint(pName, pValue);
    }

    /// <summary>Sets an SDL hint from a name and a boolean value.</summary>
    /// <param name="name">The hint name.</param>
    /// <param name="value">The boolean value to store.</param>
    /// <returns>True on success; otherwise false.</returns>
    public static SDLBool SDL_SetHint(string name, bool value) => SDL_SetHint(name, value ? "1" : "0");
    #endregion

    #region SDL_log.h

    private static SDL_LogOutputFunction? s_logCallback;

    /// <summary>Sets the minimum log priority for a log category.</summary>
    /// <param name="category">The log category.</param>
    /// <param name="priority">The minimum priority to log.</param>
    public static void SDL_SetLogPriority(SDL_LogCategory category, SDL_LogPriority priority)
    {
        SDL_SetLogPriority((int)category, priority);
    }

    /// <inheritdoc cref="SDL_SetLogOutputFunction(delegate* unmanaged{nint, int, SDL_LogPriority, byte*, void}, nint)" />
    public static void SDL_SetLogOutputFunction(SDL_LogOutputFunction? callback)
    {
        s_logCallback = callback;

        SDL_SetLogOutputFunction(callback != null ? &OnNativeMessageCallback : null, IntPtr.Zero);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe void OnNativeMessageCallback(nint userdata, int category, SDL_LogPriority priority, byte* messagePtr)
    {
        string? message = ConvertToManaged(messagePtr);

        s_logCallback?.Invoke((SDL_LogCategory)category, priority, message);
    }
    #endregion

    #region SDL_version.h, SDL_revision.h

    /// <summary>Encodes a major, minor and patch version into a single number.</summary>
    /// <param name="major">The major version.</param>
    /// <param name="minor">The minor version.</param>
    /// <param name="patch">The patch version.</param>
    /// <returns>The encoded version number.</returns>
    public static int SDL_VERSIONNUM(int major, int minor, int patch) =>
            ((major) * 1000000 + (minor) * 1000 + (patch));

    /// <summary>Extracts the major version from an encoded version number.</summary>
    /// <param name="version">The encoded version number.</param>
    /// <returns>The major version.</returns>
    public static int SDL_VERSIONNUM_MAJOR(int version) => ((version) / 1000000);

    /// <summary>Extracts the minor version from an encoded version number.</summary>
    /// <param name="version">The encoded version number.</param>
    /// <returns>The minor version.</returns>
    public static int SDL_VERSIONNUM_MINOR(int version) => (((version) / 1000) % 1000);

    /// <summary>Extracts the patch version from an encoded version number.</summary>
    /// <param name="version">The encoded version number.</param>
    /// <returns>The patch version.</returns>
    public static int SDL_VERSIONNUM_MICRO(int version) => ((version) % 1000);

    /// <summary>The SDL version this binding was built against.</summary>
    public static readonly int SDL_VERSION = SDL_VERSIONNUM(SDL_MAJOR_VERSION, SDL_MINOR_VERSION, SDL_MICRO_VERSION);

    /// <summary>Checks whether <see cref="SDL_VERSION"/> is at least the specified version.</summary>
    /// <param name="X">The required major version.</param>
    /// <param name="Y">The required minor version.</param>
    /// <param name="Z">The required patch version.</param>
    /// <returns>True when the current version is at least the requested one.</returns>
    public static bool SDL_VERSION_ATLEAST(int X, int Y, int Z) => SDL_VERSION >= SDL_VERSIONNUM(X, Y, Z);
    #endregion

    #region SDL_video.h

    /// <summary>Mask identifying an undefined window position.</summary>
    public const uint SDL_WINDOWPOS_UNDEFINED_MASK = 0x1FFF0000;
    /// <summary>Mask identifying a centered window position.</summary>
    public const uint SDL_WINDOWPOS_CENTERED_MASK = 0x2FFF0000;
    /// <summary>Undefined window position.</summary>
    public const int SDL_WINDOWPOS_UNDEFINED = 0x1FFF0000;
    /// <summary>Centered window position.</summary>
    public const int SDL_WINDOWPOS_CENTERED = 0x2FFF0000;

    /// <summary>Returns the undefined window position for the given display.</summary>
    /// <param name="X">The display identifier.</param>
    /// <returns>The encoded window position.</returns>
    public static uint SDL_WINDOWPOS_UNDEFINED_DISPLAY(SDL_DisplayID X) => (SDL_WINDOWPOS_UNDEFINED_MASK | (uint)X);

    /// <summary>Checks whether a window position is undefined.</summary>
    /// <param name="X">The encoded window position.</param>
    /// <returns>True when the position is undefined.</returns>
    public static bool SDL_WINDOWPOS_ISUNDEFINED(uint X) => (X & 0xFFFF0000) == SDL_WINDOWPOS_UNDEFINED_MASK;

    /// <summary>Returns the centered window position for the given display.</summary>
    /// <param name="X">The display identifier.</param>
    /// <returns>The encoded window position.</returns>
    public static uint SDL_WINDOWPOS_CENTERED_DISPLAY(SDL_DisplayID X) => (SDL_WINDOWPOS_CENTERED_MASK | (uint)X);

    /// <summary>Checks whether a window position is centered.</summary>
    /// <param name="X">The encoded window position.</param>
    /// <returns>True when the position is centered.</returns>
    public static bool SDL_WINDOWPOS_ISCENTERED(uint X)
    {
        return (X & 0xFFFF0000) == SDL_WINDOWPOS_CENTERED_MASK;
    }

    /// <summary>Returns the identifiers of all connected displays.</summary>
    /// <returns>A span over the display identifiers.</returns>
    public static ReadOnlySpan<SDL_DisplayID> SDL_GetDisplays()
    {
        SDL_DisplayID* displaysPtr = SDL_GetDisplays(out int count);
        return new(displaysPtr, count);
    }

    //public static ReadOnlySpan<SDL_DisplayMode> SDL_GetFullscreenDisplayModes(SDL_DisplayID displayID)
    //{
    //    SDL_DisplayMode** displaysModePtr = SDL_GetFullscreenDisplayModes(displayID, out int count);
    //    return new(displaysModePtr, count);
    //}

    /// <summary>Returns the display containing the given point.</summary>
    /// <param name="point">The point to locate.</param>
    /// <returns>The display identifier, or zero when no display contains the point.</returns>
    [LibraryImport(LibName)]
    public static partial SDL_DisplayID SDL_GetDisplayForPoint(in Point point);

    /// <summary>Returns the display with the largest intersection with the given rectangle.</summary>
    /// <param name="rect">The rectangle to locate.</param>
    /// <returns>The display identifier, or zero when no display intersects the rectangle.</returns>
    [LibraryImport(LibName)]
    public static partial SDL_DisplayID SDL_GetDisplayForRect(in Rectangle rect);

    /// <summary>Sets a boolean OpenGL attribute.</summary>
    /// <param name="attr">The attribute to set.</param>
    /// <param name="value">The boolean value.</param>
    /// <returns>True on success; otherwise false.</returns>
    public static SDLBool SDL_GL_SetAttribute(SDL_GLAttr attr, bool value) => SDL_GL_SetAttribute(attr, value ? 1 : 0);

    /// <summary>Sets an OpenGL profile attribute.</summary>
    /// <param name="attr">The attribute to set.</param>
    /// <param name="profile">The profile value.</param>
    /// <returns>True on success; otherwise false.</returns>
    public static SDLBool SDL_GL_SetAttribute(SDL_GLAttr attr, SDL_GLProfile profile) => SDL_GL_SetAttribute(attr, (int)profile);
    #endregion

    /// <summary>Polls the next pending event from the event queue.</summary>
    /// <param name="event">The next event, when one is available.</param>
    /// <returns>True when an event was returned; otherwise false.</returns>
    [LibraryImport(LibName, EntryPoint = "SDL_PollEvent")]
    public static partial SDLBool SDL_PollEvent(out SDL_Event @event);

    /// <summary>Peeks or removes events from the event queue.</summary>
    /// <param name="events">The buffer that receives the events.</param>
    /// <param name="action">Whether to peek or remove the events.</param>
    /// <param name="minType">The minimum event type to include.</param>
    /// <param name="maxType">The maximum event type to include.</param>
    /// <returns>The number of events affected.</returns>
    public static int SDL_PeepEvents(SDL_Event[] events, SDL_EventAction action, SDL_EventType minType, SDL_EventType maxType)
    {
        fixed (SDL_Event* eventsPtr = events)
            return SDL_PeepEvents(eventsPtr, events.Length, action, minType, maxType);
    }

    /// <summary>Peeks or removes events from the event queue.</summary>
    /// <param name="events">The buffer that receives the events.</param>
    /// <param name="action">Whether to peek or remove the events.</param>
    /// <param name="minType">The minimum event type to include.</param>
    /// <param name="maxType">The maximum event type to include.</param>
    /// <returns>The number of events affected.</returns>
    public static int SDL_PeepEvents(Span<SDL_Event> events, SDL_EventAction action, SDL_EventType minType, SDL_EventType maxType)
    {
        fixed (SDL_Event* eventsPtr = events)
            return SDL_PeepEvents(eventsPtr, events.Length, action, minType, maxType);
    }

    #region SDL_vulkan.h
    /// <summary>Loads the Vulkan library using the default configuration.</summary>
    /// <returns>True on success; otherwise false.</returns>
    public static SDLBool SDL_Vulkan_LoadLibrary() => SDL_Vulkan_LoadLibrary((byte*)null);

    /// <summary>Returns the Vulkan instance extensions required by SDL.</summary>
    /// <returns>The extension names.</returns>
    public static string[] SDL_Vulkan_GetInstanceExtensions()
    {
        byte** strings = SDL_Vulkan_GetInstanceExtensions(out uint count);
        string[] names = new string[count];
        for (int i = 0; i < count; i++)
        {
            names[i] = ConvertToManaged(strings[i])!;
        }

        return names;
    }
    #endregion

    #region SDL_syswm.h
    /// <summary>Size in bytes of the version 1 SDL syswm info structure.</summary>
    public static readonly int SDL_SYSWM_INFO_SIZE_V1 = (16 * (sizeof(void*) >= 8 ? sizeof(void*) : sizeof(ulong)));
    /// <summary>Size in bytes of the current SDL syswm info structure.</summary>
    public static readonly int SDL_SYSWM_CURRENT_INFO_SIZE = SDL_SYSWM_INFO_SIZE_V1;
    #endregion

    #region SDL_events.h

    //[DllImport(LibName, EntryPoint = nameof(SDL_GetEventFilter), CallingConvention = CallingConvention.Cdecl)]
    //private static extern SDL_bool SDL_GetEventFilter(out nint filter, out nint userdata);

    //public static bool SDL_GetEventFilter(out SDL_EventFilter? filter, out nint userdata)
    //{
    //    bool retVal = SDL_GetEventFilter(out nint result, out userdata) == SDL_TRUE;
    //    if (result != 0)
    //    {
    //        filter = Marshal.GetDelegateForFunctionPointer<SDL_EventFilter>(result);
    //    }
    //    else
    //    {
    //        filter = null;
    //    }

    //    return retVal;
    //}
    #endregion

    #region SDL_keycode.h
    /// <summary>Converts a scancode to a keycode.</summary>
    /// <param name="X">The scancode to convert.</param>
    /// <returns>The corresponding keycode.</returns>
    public static int SDL_SCANCODE_TO_KEYCODE(SDL_Scancode X)
    {
        return ((int)X | (int)SDLK_SCANCODE_MASK);
    }
    #endregion

    #region SDL_timer.h
    /// <summary>Converts milliseconds to nanoseconds.</summary>
    /// <param name="MS">The duration in milliseconds.</param>
    /// <returns>The duration in nanoseconds.</returns>
    public static ulong SDL_MS_TO_NS(ulong MS) => MS * SDL_NS_PER_MS;
    /// <summary>Converts nanoseconds to milliseconds.</summary>
    /// <param name="NS">The duration in nanoseconds.</param>
    /// <returns>The duration in milliseconds.</returns>
    public static ulong SDL_NS_TO_MS(ulong NS) => NS / SDL_NS_PER_MS;
    /// <summary>Converts microseconds to nanoseconds.</summary>
    /// <param name="US">The duration in microseconds.</param>
    /// <returns>The duration in nanoseconds.</returns>
    public static ulong SDL_US_TO_NS(ulong US) => US * SDL_NS_PER_US;
    /// <summary>Converts nanoseconds to microseconds.</summary>
    /// <param name="NS">The duration in nanoseconds.</param>
    /// <returns>The duration in microseconds.</returns>
    public static ulong SDL_NS_TO_US(ulong NS) => NS / SDL_NS_PER_US;
    #endregion


    #region Marshal
    /// <summary>Converts an unmanaged string to a managed version.</summary>
    /// <param name="unmanaged">The unmanaged string to convert.</param>
    /// <returns>A managed string.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string? ConvertToManaged(byte* unmanaged) => Utf8CustomMarshaller.ConvertToManaged(unmanaged);

    /// <summary>Converts an unmanaged string to a managed version.</summary>
    /// <param name="unmanaged">The unmanaged string to convert.</param>
    /// <returns>A managed string.</returns>
    public static string? ConvertToManaged(byte* unmanaged, int maxLength) => Utf8CustomMarshaller.ConvertToManaged(unmanaged, maxLength);
    #endregion
}
