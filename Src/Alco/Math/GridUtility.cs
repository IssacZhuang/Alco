using System;
using System.Collections.Generic;
using System.Numerics;

using static Alco.math;

namespace Alco;

/// <summary>
/// Provides utility methods for grid operations, especially for radial patterns.
/// </summary>
public static class GridUtility
{
    /// <summary>
    /// The total number of points in the precomputed radial pattern.
    /// </summary>
    public const int RadialPatternCount = 10000;

    /// <summary>
    /// The radius (in grid cells) used to generate the radial pattern.
    /// </summary>
    public const int RadialPatternRadius = 60;

    private static readonly int2[] _radialPattern;
    private static readonly float[] RadialPatternRadii;

    /// <summary>
    /// Gets the precomputed radial pattern of grid points, sorted by distance from the center.
    /// </summary>
    public static IReadOnlyList<int2> RadialPattern => _radialPattern;

    static GridUtility()
    {
        int2[] radialPattern = new int2[RadialPatternCount];
        float[] radialPatternRadii = new float[RadialPatternCount];

        List<int2> list = new List<int2>();
        for (int i = -RadialPatternRadius; i < RadialPatternRadius; i++)
        {
            for (int j = -RadialPatternRadius; j < RadialPatternRadius; j++)
            {
                list.Add(new int2(i, j));
            }
        }

        list.Sort((int2 A, int2 B) =>
        {
            float squaredDistanceA = A.X * (float)A.X + A.Y * (float)A.Y;
            float squaredDistanceB = B.X * (float)B.X + B.Y * (float)B.Y;
            if (squaredDistanceA < squaredDistanceB)
            {
                return -1;
            }
            return (squaredDistanceA != squaredDistanceB) ? 1 : 0;
        });

        for (int i = 0; i < RadialPatternCount; i++)
        {
            radialPattern[i] = list[i];
            int2 pos = list[i];
            radialPatternRadii[i] = sqrt(pos.X * (float)pos.X + pos.Y * (float)pos.Y);
        }

        _radialPattern = radialPattern;
        RadialPatternRadii = radialPatternRadii;
    }

    /// <summary>
    /// Get the number of cells in a radius
    /// </summary>
    /// <param name="radius">The radius</param>
    /// <returns>The number of cells in the radius</returns>
    public static int GetCellCountInRadius(float radius)
    {
        float searchRadius = radius + float.Epsilon;
        int searchIndex = Array.BinarySearch(RadialPatternRadii, searchRadius);
        if (searchIndex < 0)
        {
            return ~searchIndex;
        }
        for (int i = searchIndex; i < RadialPatternCount; i++)
        {
            if (RadialPatternRadii[i] > searchRadius)
            {
                return i;
            }
        }
        return RadialPatternCount;
    }

    /// <summary>
    /// Get the cells positions in a radius
    /// </summary>
    /// <param name="radius">The radius</param>
    /// <param name="cells">The list of cells</param>
    public static void FillCellsInRadius(List<int2> cells, float radius)
    {
        cells.Clear();
        int count = GetCellCountInRadius(radius);
        for (int i = 0; i < count; i++)
        {
            cells.Add(_radialPattern[i]);
        }
    }

    /// <summary>
    /// Gets the positions of the grid cells within the specified radius.
    /// </summary>
    /// <param name="radius">The radius to include cells, in grid units.</param>
    /// <returns>A read-only memory containing the cell positions within the radius.</returns>
    public static ReadOnlyMemory<int2> GetCellsInRadius(float radius)
    {
        int count = GetCellCountInRadius(radius);
        return new ReadOnlyMemory<int2>(_radialPattern, 0, count);
    }

    /// <summary>
    /// Computes grid cells along a straight line between two points using Bresenham's algorithm.
    /// </summary>
    /// <param name="output">The list to fill with grid coordinates on the line. The list is cleared first.</param>
    /// <param name="start">The start grid coordinate (inclusive).</param>
    /// <param name="end">The end grid coordinate (inclusive).</param>
    public static void GetBresenhamLine(ICollection<int2> output, int2 start, int2 end)
    {
        int startX = start.X;
        int startY = start.Y;
        int endX = end.X;
        int endY = end.Y;

        output.Clear();

        int deltaX = Math.Abs(endX - startX);
        int deltaY = Math.Abs(endY - startY);
        int signX = (startX < endX) ? 1 : -1;
        int signY = (startY < endY) ? 1 : -1;

        // Using the (dx + (-dy)) formulation from the canonical integer Bresenham variant
        int negativeDeltaY = -deltaY;
        int error = deltaX + negativeDeltaY;

        // Safe guard bounded by the maximum number of points on the line
        int maxSteps = Math.Max(deltaX, deltaY) + 1;
        while (true)
        {
            output.Add(new int2(startX, startY));
            if (startX == endX && startY == endY)
            {
                break;
            }
            int doubleError = 2 * error;
            if (doubleError >= negativeDeltaY)
            {
                error += negativeDeltaY;
                startX += signX;
            }
            if (doubleError <= deltaX)
            {
                error += deltaX;
                startY += signY;
            }
            maxSteps--;
            if (maxSteps <= 0)
            {
                break;
            }
        }
    }

    public static int GetBresenhamLine(Span<int2> output, int2 start, int2 end)
    {
        int startX = start.X;
        int startY = start.Y;
        int endX = end.X;
        int endY = end.Y;

        int deltaX = Math.Abs(endX - startX);
        int deltaY = Math.Abs(endY - startY);
        int signX = (startX < endX) ? 1 : -1;
        int signY = (startY < endY) ? 1 : -1;

        int negativeDeltaY = -deltaY;
        int error = deltaX + negativeDeltaY;

        int maxSteps = Math.Max(deltaX, deltaY) + 1;
        int written = 0;
        while (true)
        {
            if (written >= output.Length)
            {
                break;
            }
            output[written++] = new int2(startX, startY);
            if (startX == endX && startY == endY)
            {
                break;
            }
            int doubleError = 2 * error;
            if (doubleError >= negativeDeltaY)
            {
                error += negativeDeltaY;
                startX += signX;
            }
            if (doubleError <= deltaX)
            {
                error += deltaX;
                startY += signY;
            }
            maxSteps--;
            if (maxSteps <= 0)
            {
                break;
            }
        }
        return written;
    }

    /// <summary>
    /// Traverses all grid cells intersected by the line segment from <paramref name="start"/> to <paramref name="end"/>
    /// using the 2D DDA (Amanatides–Woo) algorithm.
    /// </summary>
    /// <remarks>
    /// Accepts floating-point endpoints and enumerates each grid cell crossed by the segment.
    /// When the line hits a grid corner (tMaxX == tMaxY), both axes advance in the same step.
    /// </remarks>
    /// <param name="output">Collection filled with traversed cell coordinates. It is cleared first.</param>
    /// <param name="start">Start point in continuous space.</param>
    /// <param name="end">End point in continuous space.</param>
    public static void GetSupercoverLineCornerBased(ICollection<int2> output, Vector2 start, Vector2 end)
    {
        output.Clear();

        int x = (int)MathF.Floor(start.X);
        int y = (int)MathF.Floor(start.Y);
        int targetX = (int)MathF.Floor(end.X);
        int targetY = (int)MathF.Floor(end.Y);

        float dx = end.X - start.X;
        float dy = end.Y - start.Y;

        int stepX = dx > 0f ? 1 : dx < 0f ? -1 : 0;
        int stepY = dy > 0f ? 1 : dy < 0f ? -1 : 0;

        float invAbsDx = stepX != 0 ? 1.0f / MathF.Abs(dx) : float.PositiveInfinity;
        float invAbsDy = stepY != 0 ? 1.0f / MathF.Abs(dy) : float.PositiveInfinity;

        float fracX = start.X - MathF.Floor(start.X);
        float fracY = start.Y - MathF.Floor(start.Y);
        float tMaxX = stepX > 0 ? (1f - fracX) * invAbsDx : stepX < 0 ? (fracX) * invAbsDx : float.PositiveInfinity;
        float tMaxY = stepY > 0 ? (1f - fracY) * invAbsDy : stepY < 0 ? (fracY) * invAbsDy : float.PositiveInfinity;

        float tDeltaX = invAbsDx;
        float tDeltaY = invAbsDy;

        while (true)
        {
            output.Add(new int2(x, y));
            if (x == targetX && y == targetY)
            {
                break;
            }

            if (tMaxX < tMaxY)
            {
                x += stepX;
                tMaxX += tDeltaX;
            }
            else if (tMaxY < tMaxX)
            {
                y += stepY;
                tMaxY += tDeltaY;
            }
            else
            {
                x += stepX;
                y += stepY;
                tMaxX += tDeltaX;
                tMaxY += tDeltaY;
            }
        }
    }

    public static int GetSupercoverLineCornerBased(Span<int2> output, Vector2 start, Vector2 end)
    {
        int x = (int)MathF.Floor(start.X);
        int y = (int)MathF.Floor(start.Y);
        int targetX = (int)MathF.Floor(end.X);
        int targetY = (int)MathF.Floor(end.Y);

        float dx = end.X - start.X;
        float dy = end.Y - start.Y;

        int stepX = dx > 0f ? 1 : dx < 0f ? -1 : 0;
        int stepY = dy > 0f ? 1 : dy < 0f ? -1 : 0;

        float invAbsDx = stepX != 0 ? 1.0f / MathF.Abs(dx) : float.PositiveInfinity;
        float invAbsDy = stepY != 0 ? 1.0f / MathF.Abs(dy) : float.PositiveInfinity;

        float fracX = start.X - MathF.Floor(start.X);
        float fracY = start.Y - MathF.Floor(start.Y);
        float tMaxX = stepX > 0 ? (1f - fracX) * invAbsDx : stepX < 0 ? (fracX) * invAbsDx : float.PositiveInfinity;
        float tMaxY = stepY > 0 ? (1f - fracY) * invAbsDy : stepY < 0 ? (fracY) * invAbsDy : float.PositiveInfinity;

        float tDeltaX = invAbsDx;
        float tDeltaY = invAbsDy;

        int written = 0;
        while (true)
        {
            if (written >= output.Length)
            {
                break;
            }
            output[written++] = new int2(x, y);
            if (x == targetX && y == targetY)
            {
                break;
            }

            if (tMaxX < tMaxY)
            {
                x += stepX;
                tMaxX += tDeltaX;
            }
            else if (tMaxY < tMaxX)
            {
                y += stepY;
                tMaxY += tDeltaY;
            }
            else
            {
                x += stepX;
                y += stepY;
                tMaxX += tDeltaX;
                tMaxY += tDeltaY;
            }
        }
        return written;
    }

    /// <summary>
    /// Traverses all grid cells intersected by the line segment when grid indices are center-based.
    /// The first cell's center is at (0, 0), so cell boundaries are at half-integers.
    /// This delegates to the corner-based implementation by offsetting coordinates by (+0.5, +0.5).
    /// </summary>
    /// <param name="output">Collection filled with traversed cell coordinates. It is cleared first.</param>
    /// <param name="start">Start point in center-based continuous space.</param>
    /// <param name="end">End point in center-based continuous space.</param>
    public static void GetSupercoverLineCenterBased(ICollection<int2> output, Vector2 start, Vector2 end)
    {
        Vector2 offset = new Vector2(0.5f, 0.5f);
        GetSupercoverLineCornerBased(output, start + offset, end + offset);
    }

    /// <summary>
    /// Span variant for center-based supercover line traversal.
    /// </summary>
    /// <param name="output">Destination span for traversed cell coordinates.</param>
    /// <param name="start">Start point in center-based continuous space.</param>
    /// <param name="end">End point in center-based continuous space.</param>
    /// <returns>The number of cells written to <paramref name="output"/>.</returns>
    public static int GetSupercoverLineCenterBased(Span<int2> output, Vector2 start, Vector2 end)
    {
        Vector2 offset = new Vector2(0.5f, 0.5f);
        return GetSupercoverLineCornerBased(output, start + offset, end + offset);
    }

    /// <summary>
    /// Computes an upper bound on the number of grid cells visited by a supercover line
    /// between two points. This matches the capacity requirement of GetSupercoverLine* methods.
    /// Formula: ceil(|dx|) + ceil(|dy|) + 1 (inclusive of the starting cell).
    /// </summary>
    /// <param name="start">Start point in continuous space.</param>
    /// <param name="end">End point in continuous space.</param>
    /// <returns>Maximum number of cells potentially traversed.</returns>
    public static int GetMaxCellsOfSupercoverLine(Vector2 start, Vector2 end)
    {
        float dx = MathF.Abs(end.X - start.X);
        float dy = MathF.Abs(end.Y - start.Y);
        int maxCells = (int)MathF.Ceiling(dx) + (int)MathF.Ceiling(dy) + 1;
        return (maxCells < 1) ? 1 : maxCells;
    }
}

