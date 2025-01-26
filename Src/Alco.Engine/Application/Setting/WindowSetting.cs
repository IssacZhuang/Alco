using Alco.Graphics;

namespace Alco.Engine;

/// <summary>
/// The window setting
/// </summary>
public struct WindowSetting
{
    public WindowSetting(int width, int height, string title)
    {
        Width = width;
        Height = height;
        Title = title;
    }

    /// <summary>
    /// The width of the window. (unit: pixel)
    /// </summary>
    public int Width { get; set; }
    /// <summary>
    /// The height of the window. (unit: pixel)
    /// </summary>
    public int Height { get; set; }
    /// <summary>
    /// The title of the window.
    /// </summary>
    public string Title { get; set; }

    /// <summary>
    /// The frame rate will be locked to the monitor refresh rate if this is true.
    /// </summary>
    public bool VSync { get; set; }

    /// <summary>
    /// The window mode
    /// </summary>
    public WindowMode WindowMode { get; set; }

    /// <summary>
    /// Is the window borderless
    /// </summary>
    public bool IsBorderless { get; set; }

    /// <summary>
    /// Is the window transparent, otherwise it will be black background
    /// </summary>
    /// <value></value>
    public bool IsTransparent { get; set; }

    /// <summary>
    /// Use Wayland window on Linux
    /// </summary>
    public bool LinuxUseWayland { get; set; }

    /// <summary>
    /// The default window setting
    /// </summary>
    public static readonly WindowSetting Default = new WindowSetting
    {
        Width = 640,
        Height = 360,
        Title = "Alco",
        WindowMode = WindowMode.Normal,
        VSync = false,
        LinuxUseWayland = false
    };

}