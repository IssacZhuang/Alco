namespace Vocore.GUI;

public enum TextAlignHorizontal
{
    Left,
    Center,
    Right
}

public enum TextAlignVertical
{
    Top,
    Center,
    Bottom
}

public static class UtilsTextAlign
{
    public static float GetPivotX(TextAlignHorizontal align)
    {
        return align switch
        {
            TextAlignHorizontal.Left => -0.5f,
            TextAlignHorizontal.Center => 0,
            TextAlignHorizontal.Right => 0.5f,
            _ => 0
        };
    }

    public static float GetPivotY(TextAlignVertical align)
    {
        return align switch
        {
            TextAlignVertical.Top => 0.5f,
            TextAlignVertical.Center => 0,
            TextAlignVertical.Bottom => -0.5f,
            _ => 0
        };
    }
}