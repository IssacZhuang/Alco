using System;
using System.Collections.Generic;
using System.Numerics;

namespace Alco;

public struct CurvePoint<T> : IComparable<CurvePoint<T>>
{
    public float Key;
    public T Value;

    public CurvePoint(float key, T value)
    {
        this.Key = key;
        this.Value = value;
    }

    public int CompareTo(CurvePoint<T> other)
    {
        return this.Key.CompareTo(other.Key);
    }
}
