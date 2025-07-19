using System.Numerics;
using Alco;
using static Alco.math;

/// <summary>
/// Provides utility methods for coordinate space transformations.
/// </summary>
public static class UtilsCoordinates
{
    /// <summary>
    /// Converts pixel coordinates to world space coordinates.
    /// </summary>
    /// <remarks>
    /// Input coordinates use pixel space: origin (0,0) at top-left, X points right, Y points down.
    /// Output coordinates use world space: origin (0,0) at center, X points right, Y points up.
    /// </remarks>
    /// <param name="pixelPosition">The pixel coordinates to convert</param>
    /// <param name="canvasSize">The size of the canvas</param>
    /// <returns>The world position corresponding to the pixel coordinates</returns>
    public static Vector2 PixelSpaceToWorldSpace(int2 pixelPosition, int2 canvasSize)
    {
        return new Vector2(pixelPosition.X - (canvasSize.X - 1) * 0.5f, -pixelPosition.Y + (canvasSize.Y - 1) * 0.5f);
    }

    /// <summary>
    /// Converts pixel coordinates to world space coordinates.
    /// </summary>
    /// <remarks>
    /// Input coordinates use pixel space: origin (0,0) at top-left, X points right, Y points down.
    /// Output coordinates use world space: origin (0,0) at center, X points right, Y points up.
    /// </remarks>
    /// <param name="pixelPosition">The pixel coordinates to convert</param>
    /// <param name="canvasWidth">The width of the canvas</param>
    /// <param name="canvasHeight">The height of the canvas</param>
    /// <returns>The world position corresponding to the pixel coordinates</returns>
    public static Vector2 PixelSpaceToWorldSpace(int2 pixelPosition, int canvasWidth, int canvasHeight)
    {
        return new Vector2(pixelPosition.X - (canvasWidth - 1) * 0.5f, -pixelPosition.Y + (canvasHeight - 1) * 0.5f);
    }

    /// <summary>
    /// Converts world space coordinates to pixel coordinates.
    /// </summary>
    /// <remarks>
    /// Input coordinates use world space: origin (0,0) at center, X points right, Y points up.
    /// Output coordinates use pixel space: origin (0,0) at top-left, X points right, Y points down.
    /// </remarks>
    /// <param name="worldPosition">The world position to convert</param>
    /// <param name="canvasWidth">The width of the canvas</param>
    /// <param name="canvasHeight">The height of the canvas</param>
    /// <returns>The pixel position corresponding to the world position</returns>
    public static int2 WorldSpaceToPixelSpace(Vector2 worldPosition, int canvasWidth, int canvasHeight)
    {
        float x = worldPosition.X + (canvasWidth - 1) * 0.5f;
        float y = (canvasHeight - 1) * 0.5f - worldPosition.Y;
        return new int2(round(x), round(y));
    }

    /// <summary>
    /// Converts world space coordinates to pixel coordinates.
    /// </summary>
    /// <remarks>
    /// Input coordinates use world space: origin (0,0) at center, X points right, Y points up.
    /// Output coordinates use pixel space: origin (0,0) at top-left, X points right, Y points down.
    /// </remarks>
    /// <param name="worldPosition">The world position to convert</param>
    /// <param name="canvasSize">The size of the canvas</param>
    /// <returns>The pixel position corresponding to the world position</returns>
    public static int2 WorldSpaceToPixelSpace(Vector2 worldPosition, int2 canvasSize)
    {
        float x = worldPosition.X + (canvasSize.X - 1) * 0.5f;
        float y = (canvasSize.Y - 1) * 0.5f - worldPosition.Y;
        return new int2(round(x), round(y));
    }
}