using System;
using System.Collections.Generic;
using System.Numerics;

namespace Alco;

public struct CurvePoint<T> : IComparable<CurvePoint<T>>
{
    public float Time;
    public T Value;

    public CurvePoint(float time, T value)
    {
        this.Time = time;
        this.Value = value;
    }

    public int CompareTo(CurvePoint<T> other)
    {
        return this.Time.CompareTo(other.Time);
    }
}
