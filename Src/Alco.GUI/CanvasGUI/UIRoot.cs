namespace Alco.GUI;

public sealed class UIRoot : UINode
{
    public Canvas Canvas { get; }

    public UIRoot(Canvas canvas)
    {
        Canvas = canvas;
    }
}