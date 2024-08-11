using Vocore.Graphics;

namespace Vocore.Engine;

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
    /// The window is disabled
    /// </summary>
    public bool IsWindowDisabled { get; set; }

    /// <summary>
    /// The window mode
    /// </summary>
    public WindowMode WindowMode { get; set; }

    /// <summary>
    /// The default window setting
    /// </summary>
    public static readonly WindowSetting Default = new WindowSetting
    {
        Width = 640,
        Height = 360,
        Title = "Vocore",
        IsWindowDisabled = false,
        VSync = false
    };


    /// <summary>
    /// The window is disabled
    /// </summary>
    public static readonly WindowSetting NoWindow = new WindowSetting
    {
        Width = 0,
        Height = 0,
        Title = "Vocore",
        IsWindowDisabled = true,
        VSync = false
    };
}