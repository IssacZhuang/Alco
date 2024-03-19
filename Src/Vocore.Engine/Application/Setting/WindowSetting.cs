namespace Vocore.Engine;

/// <summary>
/// The window setting
/// </summary>
public struct WindowSetting
{
    public WindowSetting(int wWidth, int height, string title)
    {
        Width = wWidth;
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
    /// The default window setting
    /// </summary>
    public static readonly WindowSetting Default = new WindowSetting
    {
        Width = 640,
        Height = 360,
        Title = "Vocore"
    };
}