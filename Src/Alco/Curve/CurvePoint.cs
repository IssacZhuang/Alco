using System;
using System.Collections.Generic;
using System.Numerics;

namespace Alco;

/// <summary>
/// Represents a point on a curve with time and float value
/// </summary>
public struct CurvePointValue : IComparable<CurvePointValue>, ISortable
{
    public float Time;
    public float Value;

    public CurvePointValue(float t, float value)
    {
        this.Time = t;
        this.Value = value;
    }

    public float SortKey => this.Time;

    public int CompareTo(CurvePointValue other)
    {
        return this.Time.CompareTo(other.Time);
    }
}

/// <summary>
/// Represents a point on a 2D curve with time and Vector2 value
/// </summary>
public struct CurvePoint2Value : IComparable<CurvePoint2Value>, ISortable
{
    public float Time;
    public Vector2 Value;

    public CurvePoint2Value(float t, Vector2 value)
    {
        this.Time = t;
        this.Value = value;
    }

    public float SortKey => this.Time;

    public int CompareTo(CurvePoint2Value other)
    {
        return this.Time.CompareTo(other.Time);
    }
}

/// <summary>
/// Represents a point on a 3D curve with time and Vector3 value
/// </summary>
public struct CurvePoint3Value : IComparable<CurvePoint3Value>, ISortable
{
    public float Time;
    public Vector3 Value;

    public CurvePoint3Value(float t, Vector3 value)
    {
        this.Time = t;
        this.Value = value;
    }

    public float SortKey => this.Time;

    public int CompareTo(CurvePoint3Value other)
    {
        return this.Time.CompareTo(other.Time);
    }
}

/// <summary>
/// Represents a point on a 4D curve with time and Vector4 value
/// </summary>
public struct CurvePoint4Value : IComparable<CurvePoint4Value>, ISortable
{
    public float Time;
    public Vector4 Value;

    public CurvePoint4Value(float t, Vector4 value)
    {
        this.Time = t;
        this.Value = value;
    }

    public float SortKey => this.Time;

    public int CompareTo(CurvePoint4Value other)
    {
        return this.Time.CompareTo(other.Time);
    }
}

/// <summary>
/// Represents the <see cref="CurvePointValue"/> in object type for serialization
/// </summary>
public class CurvePoint
{
    public float Time { get; set; }
    public float Value { get; set; }

    public static implicit operator CurvePointValue(CurvePoint point)
    {
        return new CurvePointValue(point.Time, point.Value);
    }
}

/// <summary>
/// Represents the <see cref="CurvePoint2Value"/> in object type for serialization
/// </summary>
public class CurvePoint2
{
    public float Time { get; set; }
    public Vector2 Value { get; set; }


    public static implicit operator CurvePoint2Value(CurvePoint2 point)
    {
        return new CurvePoint2Value(point.Time, point.Value);
    }
}

/// <summary>
/// Represents the <see cref="CurvePoint3Value"/> in object type for serialization
/// </summary>
public class CurvePoint3
{
    public float Time { get; set; }
    public Vector3 Value { get; set; }


    public static implicit operator CurvePoint3Value(CurvePoint3 point)
    {
        return new CurvePoint3Value(point.Time, point.Value);
    }
}

/// <summary>
/// Represents the <see cref="CurvePoint4Value"/> in object type for serialization
/// </summary>
public class CurvePoint4
{
    public float Time { get; set; }
    public Vector4 Value { get; set; }

    public static implicit operator CurvePoint4Value(CurvePoint4 point)
    {
        return new CurvePoint4Value(point.Time, point.Value);
    }
}

/// <summary>
/// Represents the curve point of <see cref="ColorFloat"/> in object type for serialization.
/// Can be converted to <see cref="CurvePoint4Value"/>.
/// A dedicated curve point type is provided for ColorFloat because ColorFloat can use hex string as JSON value.
/// </summary>
public class CurvePointColor
{
    public float Time { get; set; }
    public ColorFloat Value { get; set; }

    public static implicit operator CurvePoint4Value(CurvePointColor point)
    {
        return new CurvePoint4Value(point.Time, point.Value);
    }
}