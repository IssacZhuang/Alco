using System.Numerics;

namespace Vocore.GUI;



public static class UtilsCanvas
{
    public static Vector2 CanvasPointToScreen(Vector2 canvasPoint, Vector2 canvasSize, Vector2 screenSize)
    {
        float scaleX = screenSize.X / canvasSize.X;
        float scaleY = screenSize.Y / canvasSize.Y;
        //the zero point of screen is at the top left corner
        //the zero point of canvas is at the center
        return new Vector2(
            (canvasPoint.X + canvasSize.X / 2) * scaleX,
            screenSize.Y - (canvasPoint.Y + canvasSize.Y / 2) * scaleY
        );
    }

    public static Vector2 ScreenPointToCanvas(Vector2 screenPoint, Vector2 canvasSize, Vector2 screenSize)
    {   
        float scaleX = screenSize.X / canvasSize.X;
        float scaleY = screenSize.Y / canvasSize.Y;
        //the zero point of screen is at the top left corner
        //the zero point of canvas is at the center
        return new Vector2(
            screenPoint.X / scaleX - canvasSize.X / 2,
            (screenSize.Y - screenPoint.Y) / scaleY - canvasSize.Y / 2
        );
    }
}