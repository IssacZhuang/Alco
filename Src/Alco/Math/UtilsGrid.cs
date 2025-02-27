using System;
using System.Collections.Generic;

using static Alco.math;

namespace Alco;

public static class UtilsGrid
{
    public const int RadialPatternCount = 10000;
    public const int RadialPatternRadius = 60;
    public static readonly IReadOnlyList<int2> RadialPattern;
    private static readonly float[] RadialPatternRadii;

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

        RadialPattern = radialPattern;
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
    public static void GetCellsInRadius(List<int2> cells, float radius)
    {
        cells.Clear();
        int count = GetCellCountInRadius(radius);
        for (int i = 0; i < count; i++)
        {
            cells.Add(RadialPattern[i]);
        }
    }
}

