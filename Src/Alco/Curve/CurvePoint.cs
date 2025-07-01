using System;
using System.Collections.Generic;
using System.Numerics;

namespace Alco
{
    /// <summary>
    /// Represents a point on a curve with time and float value
    /// </summary>
    public struct CurvePoint : IComparable<CurvePoint>, ISortable
    {
        public float Time;
        public float Value;

        public CurvePoint(float t, float value)
        {
            this.Time = t;
            this.Value = value;
        }

        public float SortKey => this.Time;

        public int CompareTo(CurvePoint other)
        {
            return this.Time.CompareTo(other.Time);
        }
    }

    /// <summary>
    /// Represents a point on a 2D curve with time and Vector2 value
    /// </summary>
    public struct CurvePoint2 : IComparable<CurvePoint2>, ISortable
    {
        public float Time;
        public Vector2 Value;

        public CurvePoint2(float t, Vector2 value)
        {
            this.Time = t;
            this.Value = value;
        }

        public float SortKey => this.Time;

        public int CompareTo(CurvePoint2 other)
        {
            return this.Time.CompareTo(other.Time);
        }
    }

    /// <summary>
    /// Represents a point on a 3D curve with time and Vector3 value
    /// </summary>
    public struct CurvePoint3 : IComparable<CurvePoint3>, ISortable
    {
        public float Time;
        public Vector3 Value;

        public CurvePoint3(float t, Vector3 value)
        {
            this.Time = t;
            this.Value = value;
        }

        public float SortKey => this.Time;

        public int CompareTo(CurvePoint3 other)
        {
            return this.Time.CompareTo(other.Time);
        }
    }

    /// <summary>
    /// Represents a point on a 4D curve with time and Vector4 value
    /// </summary>
    public struct CurvePoint4 : IComparable<CurvePoint4>, ISortable
    {
        public float Time;
        public Vector4 Value;

        public CurvePoint4(float t, Vector4 value)
        {
            this.Time = t;
            this.Value = value;
        }

        public float SortKey => this.Time;

        public int CompareTo(CurvePoint4 other)
        {
            return this.Time.CompareTo(other.Time);
        }
    }
}

