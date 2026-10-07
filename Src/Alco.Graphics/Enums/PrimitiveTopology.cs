namespace Alco.Graphics;

/// <summary>
/// How consecutive vertices are assembled into primitives.
/// </summary>
public enum PrimitiveTopology
{
    /// <summary>Independent points, one per vertex.</summary>
    PointList = 0,
    /// <summary>Independent line segments, one per vertex pair.</summary>
    LineList = 1,
    /// <summary>A connected line strip; each vertex after the first starts a new segment.</summary>
    LineStrip = 2,
    /// <summary>Independent triangles, one per vertex triple.</summary>
    TriangleList = 3,
    /// <summary>A connected triangle strip; each vertex after the second forms a new triangle.</summary>
    TriangleStrip = 4,
}