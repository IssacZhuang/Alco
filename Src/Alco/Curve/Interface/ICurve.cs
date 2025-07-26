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
        void SetPoints(ReadOnlySpan<CurvePointValue> points);
        IReadOnlyList<CurvePointValue> Points { get; }
    }

    /// <summary>
    /// Interface for 2D curves
    /// </summary>
    public interface ICurve2D
    {
        Vector2 Evaluate(float t);
        int PointsCount { get; }
        void SetPoints(ReadOnlySpan<CurvePoint2Value> points);
        IReadOnlyList<CurvePoint2Value> Points { get; }
    }

    /// <summary>
    /// Interface for 3D curves
    /// </summary>
    public interface ICurve3D
    {
        Vector3 Evaluate(float t);
        int PointsCount { get; }
        void SetPoints(ReadOnlySpan<CurvePoint3Value> points);
        IReadOnlyList<CurvePoint3Value> Points { get; }
    }

    /// <summary>
    /// Interface for 4D curves
    /// </summary>
    public interface ICurve4D
    {
        Vector4 Evaluate(float t);
        int PointsCount { get; }
        void SetPoints(ReadOnlySpan<CurvePoint4Value> points);
        IReadOnlyList<CurvePoint4Value> Points { get; }
    }
}

