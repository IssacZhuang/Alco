using System.Numerics;

namespace Vocore.GUI;

public class UIScrollable : UINode, IScrollable
{
    public SrollMode ScrollMode { get; set; }
    public UINode? Content { get; set; }
    public void OnScroll(Vector2 displacement)
    {
        if (Content == null)
        {
            return;
        }

        if (ScrollMode.HasFlag(SrollMode.Vertical))
        {
            Content.Position = new Vector2(Content.Position.X, Content.Position.Y + displacement.Y);
        }

        if (ScrollMode.HasFlag(SrollMode.Horizontal))
        {
            Content.Position = new Vector2(Content.Position.X + displacement.X, Content.Position.Y);
        }

    }
}