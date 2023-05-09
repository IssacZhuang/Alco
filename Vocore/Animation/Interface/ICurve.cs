using System;
using System.Collections.Generic;
using Unity.Mathematics;

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

    public interface ICurve2D: ICurveBase<float2>
    {
    }

    public interface ICurve3D: ICurveBase<float3>
    {
    }

    public interface ICurve4D: ICurveBase<float4>
    {
    }
}

