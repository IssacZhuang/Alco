using System;
using System.Collections.Generic;
using UnityEngine;

namespace Vocore
{
    public interface ICurveBase<T> where T : unmanaged
    {
        T Evaluate(float t);
        int PointsCount { get; }
        void SetPoints(IList<CurvePoint<T>> points);
        IReadOnlyList <CurvePoint<T>> Points { get; }
    }

    public interface ICurve: ICurveBase<float>
    {
    }

    public interface ICurve2D: ICurveBase<Vector2>
    {
    }

    public interface ICurve3D: ICurveBase<Vector3>
    {
    }

    public interface ICurve4D: ICurveBase<Vector4>
    {
    }
}

