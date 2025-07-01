using System;
using System.Numerics;
using System.Collections.Generic;


namespace Alco
{
    /// <summary>
    /// Interface for single-dimensional curves
    /// </summary>
    public interface ICurve
    {
        float Evaluate(float t);
        int PointsCount { get; }
        void SetPoints(IReadOnlyList<CurvePoint> points);
        IReadOnlyList<CurvePoint> Points { get; }
    }

    /// <summary>
    /// Interface for 2D curves
    /// </summary>
    public interface ICurve2D
    {
        Vector2 Evaluate(float t);
        int PointsCount { get; }
        void SetPoints(IReadOnlyList<CurvePoint2> points);
        IReadOnlyList<CurvePoint2> Points { get; }
    }

    /// <summary>
    /// Interface for 3D curves
    /// </summary>
    public interface ICurve3D
    {
        Vector3 Evaluate(float t);
        int PointsCount { get; }
        void SetPoints(IReadOnlyList<CurvePoint3> points);
        IReadOnlyList<CurvePoint3> Points { get; }
    }

    /// <summary>
    /// Interface for 4D curves
    /// </summary>
    public interface ICurve4D
    {
        Vector4 Evaluate(float t);
        int PointsCount { get; }
        void SetPoints(IReadOnlyList<CurvePoint4> points);
        IReadOnlyList<CurvePoint4> Points { get; }
    }
}

