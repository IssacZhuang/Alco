namespace Vocore.Engine;

public struct WindowSetting
{
    public WindowSetting(int wWidth, int height, string title)
    {
        Width = wWidth;
        Height = height;
        Title = title;
    }

    public int Width { get; set; }
    public int Height { get; set; }
    public string Title { get; set; }

    public static readonly WindowSetting Default = new WindowSetting
    {
        Width = 640,
        Height = 360,
        Title = "Vocore"
    };
}