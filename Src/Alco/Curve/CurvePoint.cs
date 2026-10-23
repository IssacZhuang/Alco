using System;
using System.Collections.Generic;
using System.Numerics;

namespace Alco;

/// <summary>
/// A key and value pair on a curve, ordered by key.
/// </summary>
/// <typeparam name="T">The value type at this point.</typeparam>
public struct CurvePoint<T> : IComparable<CurvePoint<T>>
{
    public float Key;
    public T Value;

    /// <summary>
    /// Initializes a point with the given key and value.
    /// </summary>
    /// <param name="key">The position of the point along the curve.</param>
    /// <param name="value">The value at the point.</param>
    public CurvePoint(float key, T value)
    {
        this.Key = key;
        this.Value = value;
    }

    /// <summary>
    /// Compares this point with another by key.
    /// </summary>
    /// <param name="other">The point to compare against.</param>
    /// <returns>A negative value, zero, or a positive value when this key is less than, equal to, or greater than the other key.</returns>
    public int CompareTo(CurvePoint<T> other)
    {
        return this.Key.CompareTo(other.Key);
    }
}
