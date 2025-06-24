using System;
using System.Collections.Generic;

using static Alco.math;

namespace Alco;

/// <summary>
/// Provides utility methods for grid operations, especially for radial patterns.
/// </summary>
public static class UtilsGrid
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

    static UtilsGrid()
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
}

