using System.Numerics;

namespace Vocore;

public static class UtilsCanvasMath
{
    public static Vector2 TransformAnchor(Vector2 parentSize, Anchor amchor, Vector2 localPosition)
    {
        Vector2 anchorCenter = amchor.CenterPoint;
        Vector2 offset = anchorCenter * parentSize;
        return offset + localPosition;
    }
}