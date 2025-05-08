
namespace Alco.Rendering;

public class LinkableData
{
    public Sprite Sprite;
    public object? UserData;

    public LinkableData(Sprite sprite, object? userData)
    {
        Sprite = sprite;
        UserData = userData;
    }
}