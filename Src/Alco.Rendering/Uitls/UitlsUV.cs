namespace Alco.Rendering;

public static class UtilsUV
{
    /// <summary>
    /// Calculates UV coordinates for a specific frame in a grid-based sprite sheet
    /// </summary>
    /// <param name="columnCount">Number of columns in the sprite sheet (must be positive)</param>
    /// <param name="rowCount">Number of rows in the sprite sheet (must be positive)</param>
    /// <param name="frameIndex">Zero-based frame index (must be between 0 and columnCount*rowCount-1)</param>
    /// <returns>Rect containing UV coordinates for the specified frame</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when input parameters are invalid
    /// </exception>
    public static Rect CalculateFrameUVRect(int columnCount, int rowCount, int frameIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(columnCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rowCount);
        ArgumentOutOfRangeException.ThrowIfNegative(frameIndex);

        int maxFrames = columnCount * rowCount;
        if (frameIndex >= maxFrames)
        {
            throw new ArgumentOutOfRangeException(nameof(frameIndex),
                $"Frame index must be less than {maxFrames}");
        }

        int column = frameIndex % columnCount;
        int row = frameIndex / columnCount;

        float uvWidth = 1f / columnCount;
        float uvHeight = 1f / rowCount;

        float uvX = column * uvWidth;
        float uvY = row * uvHeight;

        return new Rect(uvX, uvY, uvWidth, uvHeight);
    }
}