using System;
using System.Numerics;
using System.Collections.Generic;


namespace Alco
{
    /// <summary>
    /// Interface for single-dimensional curves
    /// </summary>
    public interface ICurve<T> where T : struct
    {
        T Evaluate(float t);
        int Count { get; }
        CurvePoint<T> this[int index] { get; }
    }
}

