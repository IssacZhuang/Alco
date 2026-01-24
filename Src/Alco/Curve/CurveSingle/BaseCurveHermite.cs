using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Alco;

/// <summary>
/// Base class for Hermite spline curves supporting generic types.
/// </summary>
/// <typeparam name="T">The type of value to interpolate (e.g. float, Vector2, Vector3).</typeparam>
public abstract class BaseCurveHermite<T> : ICurve<T>, ICollection<CurvePoint<T>> where T : struct
{
    private readonly List<CurvePoint<T>> _points = new List<CurvePoint<T>>();
    private readonly List<T> _slopes = new List<T>();
    private bool _isDirty = false;


    /// <summary>
    /// Gets the number of points in the curve.
    /// </summary>
    public int Count => _points.Count;

    /// <summary>
    /// Gets a value indicating whether the collection is read-only.
    /// </summary>
    public bool IsReadOnly => false;

    /// <summary>
    /// Initializes a new instance of the <see cref="BaseCurveHermite{T}"/> class.
    /// </summary>
    public BaseCurveHermite()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="BaseCurveHermite{T}"/> class with specified points.
    /// </summary>
    /// <param name="points">The points to initialize the curve with.</param>
    public BaseCurveHermite(ReadOnlySpan<CurvePoint<T>> points)
    {
        _points.AddRange(points);
        SetDirty();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="BaseCurveHermite{T}"/> class with specified points.
    /// </summary>
    /// <param name="points">The points to initialize the curve with.</param>
    public BaseCurveHermite(IReadOnlyList<CurvePoint<T>> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        _points.AddRange(points);
        SetDirty();
    }

    /// <summary>
    /// Gets or sets the point at the specified index.
    /// </summary>
    /// <param name="i">The index of the point.</param>
    public CurvePoint<T> this[int i]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _points[i];
    }

    /// <summary>
    /// Sets the points of the curve.
    /// </summary>
    /// <param name="points">The new points.</param>
    public void SetPoints(ReadOnlySpan<CurvePoint<T>> points)
    {
        _points.Clear();
        _points.AddRange(points);
        SetDirty();
    }

    /// <summary>
    /// Sets the points of the curve.
    /// </summary>
    /// <param name="points">The new points.</param>
    public void SetPoints(IReadOnlyList<CurvePoint<T>> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        _points.Clear();
        _points.AddRange(points);
        SetDirty();
    }

    private void SetDirty()
    {
        _isDirty = true;
    }

    private void EnsureClean()
    {
        if (!_isDirty)
        {
            return;
        }
        _points.Sort((a, b) => a.Key.CompareTo(b.Key));
        CalculateSlopes();
        _isDirty = false;
    }

    private void CalculateSlopes()
    {
        _slopes.Clear();
        int count = _points.Count;
        
        if (_slopes.Capacity < count)
        {
            _slopes.Capacity = count;
        }

        for (int i = 0; i < count; i++)
        {
            T slope;
            if (count < 2)
            {
                slope = default;
            }
            else if (i == 0)
            {
                slope = CalculateSegmentSlope(_points[i], _points[i + 1]);
            }
            else if (i == count - 1)
            {
                slope = CalculateSegmentSlope(_points[i - 1], _points[i]);
            }
            else
            {
                // Finite difference: average of slopes of adjacent segments
                T s1 = CalculateSegmentSlope(_points[i - 1], _points[i]);
                T s2 = CalculateSegmentSlope(_points[i], _points[i + 1]);
                slope = Scale(Add(s1, s2), 0.5f);
            }
            _slopes.Add(slope);
        }
    }

    private T CalculateSegmentSlope(CurvePoint<T> p1, CurvePoint<T> p2)
    {
        float dt = p2.Key - p1.Key;
        if (Math.Abs(dt) < 1e-6f)
        {
            return default;
        }
        T diff = Subtract(p2.Value, p1.Value);
        return Scale(diff, 1f / dt);
    }

    /// <summary>
    /// Evaluates the curve at the specified time.
    /// </summary>
    /// <param name="time">The time to evaluate at.</param>
    /// <returns>The interpolated value.</returns>
    public T Evaluate(float time)
    {
        EnsureClean();

        if (_points.Count == 0)
        {
            return default;
        }
        if (time <= _points[0].Key)
        {
            return _points[0].Value;
        }
        if (time >= _points[_points.Count - 1].Key)
        {
            return _points[_points.Count - 1].Value;
        }

        int i = BinarySearchFloor(time);

        float t0 = _points[i].Key;
        float t1 = _points[i + 1].Key;
        float dt = t1 - t0;
        
        if (Math.Abs(dt) < 1e-6f)
        {
             return _points[i].Value;
        }

        float tNormalized = (time - t0) / dt;
        
        T p0 = _points[i].Value;
        T p1 = _points[i + 1].Value;
        T m0 = _slopes[i];
        T m1 = _slopes[i + 1];

        // Cubic Hermite Spline basis functions
        float t2 = tNormalized * tNormalized;
        float t3 = t2 * tNormalized;
        
        float h00 = 2 * t3 - 3 * t2 + 1;
        float h10 = t3 - 2 * t2 + tNormalized;
        float h01 = -2 * t3 + 3 * t2;
        float h11 = t3 - t2;

        T term1 = Scale(p0, h00);
        T term2 = Scale(m0, h10 * dt);
        T term3 = Scale(p1, h01);
        T term4 = Scale(m1, h11 * dt);

        return Add(Add(term1, term2), Add(term3, term4));
    }

    /// <summary>
    /// Subtracts value b from value a.
    /// </summary>
    /// <param name="a">The minuend.</param>
    /// <param name="b">The subtrahend.</param>
    /// <returns>The result of a - b.</returns>
    protected abstract T Subtract(T a, T b);

    /// <summary>
    /// Adds two values.
    /// </summary>
    /// <param name="a">The first value.</param>
    /// <param name="b">The second value.</param>
    /// <returns>The result of a + b.</returns>
    protected abstract T Add(T a, T b);

    /// <summary>
    /// Scales a value by a scalar factor.
    /// </summary>
    /// <param name="a">The value to scale.</param>
    /// <param name="scalar">The scalar factor.</param>
    /// <returns>The scaled value.</returns>
    protected abstract T Scale(T a, float scalar);

    private int BinarySearchFloor(float t)
    {
        int low = 0;
        int high = _points.Count - 1;
        while (low <= high)
        {
            int mid = (low + high) / 2;
            if (t < _points[mid].Key)
            {
                high = mid - 1;
            }
            else if (t > _points[mid].Key)
            {
                low = mid + 1;
            }
            else
            {
                return mid;
            }
        }
        return high;
    }

    /// <summary>
    /// Adds a point to the curve.
    /// </summary>
    /// <param name="item">The point to add.</param>
    public void Add(CurvePoint<T> item)
    {
        _points.Add(item);
        SetDirty();
    }

    /// <summary>
    /// Removes all points from the curve.
    /// </summary>
    public void Clear()
    {
        _points.Clear();
        _slopes.Clear();
        SetDirty();
    }

    /// <summary>
    /// Determines whether the curve contains a specific point.
    /// </summary>
    /// <param name="item">The point to locate.</param>
    /// <returns>true if the point is found; otherwise, false.</returns>
    public bool Contains(CurvePoint<T> item)
    {
        return _points.Contains(item);
    }

    /// <summary>
    /// Copies the points to an array.
    /// </summary>
    /// <param name="array">The destination array.</param>
    /// <param name="arrayIndex">The zero-based index in array at which copying begins.</param>
    public void CopyTo(CurvePoint<T>[] array, int arrayIndex)
    {
        // Ensure sorted before exposing if needed, but CopyTo usually just copies underlying collection.
        // For consistency with random access (Points[i]), we don't necessarily sort here unless needed.
        // However, EnsureClean() is private. Let's just copy what we have. 
        // If strict correctness is needed, EnsureClean() should be called, but CopyTo implies raw collection copy often.
        // Given BaseCurveLinear logic, we stick to raw copy but might be unsorted if Add() was just called.
        // Users should call Evaluate to guarantee sort or we should expose Sort publicly? 
        // BaseCurveLinear doesn't expose Sort. We'll stick to raw copy.
        _points.CopyTo(array, arrayIndex);
    }

    /// <summary>
    /// Removes the first occurrence of a specific point from the curve.
    /// </summary>
    /// <param name="item">The point to remove.</param>
    /// <returns>true if the point was successfully removed; otherwise, false.</returns>
    public bool Remove(CurvePoint<T> item)
    {
        if (_points.Remove(item))
        {
            SetDirty();
            return true;
        }
        return false;
    }

    /// <summary>
    /// Returns an enumerator that iterates through the points.
    /// </summary>
    /// <returns>An enumerator.</returns>
    public IEnumerator<CurvePoint<T>> GetEnumerator()
    {
        return _points.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return _points.GetEnumerator();
    }
}

