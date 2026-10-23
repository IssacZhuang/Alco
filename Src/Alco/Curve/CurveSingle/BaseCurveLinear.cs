using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Alco;

/// <summary>
/// Base class for curves defined by linearly interpolated key points sorted by time.
/// </summary>
public abstract class BaseCurveLinear<T> :ICurve<T>, ICollection<CurvePoint<T>> where T : struct
{
    private readonly List<CurvePoint<T>> _points = new List<CurvePoint<T>>();
    private bool _isSortDirty = false;

    /// <summary>
    /// Gets the number of key points.
    /// </summary>
    public int Count => _points.Count;

    /// <summary>
    /// Gets whether the collection is read-only; always <c>false</c>.
    /// </summary>
    public bool IsReadOnly => false;

    /// <summary>
    /// Initializes an empty curve.
    /// </summary>
    public BaseCurveLinear()
    {

    }

    /// <summary>
    /// Initializes a curve from key points.
    /// </summary>
    /// <param name="points">The key points to copy.</param>
    public BaseCurveLinear(ReadOnlySpan<CurvePoint<T>> points)
    {
        _points.AddRange(points);
        Sort();
    }

    /// <summary>
    /// Initializes a curve from key points.
    /// </summary>
    /// <param name="points">The key points to copy.</param>
    public BaseCurveLinear(IReadOnlyList<CurvePoint<T>> points)
    {
        ArgumentNullException.ThrowIfNull(points);

        _points.AddRange(points);
        Sort();
    }

    public CurvePoint<T> this[int i]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _points[i];
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set
        {
            _points[i] = value;
            _isSortDirty = true;
        }
    }

    /// <summary>
    /// Replaces the curve's key points.
    /// </summary>
    /// <param name="points">The key points to copy.</param>
    public void SetPoints(ReadOnlySpan<CurvePoint<T>> points)
    {
        _points.Clear();
        _points.AddRange(points);
        Sort();
    }

    /// <summary>
    /// Replaces the curve's key points.
    /// </summary>
    /// <param name="points">The key points to copy.</param>
    public void SetPoints(IReadOnlyList<CurvePoint<T>> points)
    {
        ArgumentNullException.ThrowIfNull(points);

        _points.Clear();
        _points.AddRange(points);
        Sort();
    }

    private void Sort()
    {
        if (!_isSortDirty)
        {
            return;
        }
        _points.Sort(static (a, b) => a.Key.CompareTo(b.Key));
        _isSortDirty = false;
    }


    /// <summary>
    /// Evaluates the curve at a time, clamping outside the key-point range; an empty curve returns the default value.
    /// </summary>
    /// <param name="time">The time to evaluate.</param>
    /// <returns>The interpolated value.</returns>
    public T Evaluate(float time)
    {
        Sort(); // Ensure sorted before evaluation

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
        CurvePoint<T> keyFrame1 = _points[i];
        CurvePoint<T> keyFrame2 = _points[i + 1];
        float t = (time - keyFrame1.Key) / (keyFrame2.Key - keyFrame1.Key);
        return Lerp(keyFrame1.Value, keyFrame2.Value, t);
    }

    protected abstract T Lerp(T a, T b, float t);

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
    /// Adds a key point.
    /// </summary>
    /// <param name="item">The key point to add.</param>
    public void Add(CurvePoint<T> item)
    {
        _points.Add(item);
        _isSortDirty = true;
    }

    /// <summary>
    /// Removes a key point.
    /// </summary>
    /// <param name="item">The key point to remove.</param>
    /// <returns><c>true</c> if the key point was removed.</returns>
    public bool Remove(CurvePoint<T> item)
    {
        if (_points.Remove(item))
        {
            _isSortDirty = true;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Removes all key points.
    /// </summary>
    public void Clear()
    {
        _points.Clear();
        _isSortDirty = false;
    }

    /// <summary>
    /// Determines whether the curve contains a key point.
    /// </summary>
    /// <param name="item">The key point to locate.</param>
    /// <returns><c>true</c> if the key point is present.</returns>
    public bool Contains(CurvePoint<T> item)
    {
        return _points.Contains(item);
    }

    /// <summary>
    /// Copies the key points to an array starting at the given index.
    /// </summary>
    /// <param name="array">The destination array.</param>
    /// <param name="arrayIndex">The zero-based index in the destination array at which copying starts.</param>
    public void CopyTo(CurvePoint<T>[] array, int arrayIndex)
    {
        _points.CopyTo(array, arrayIndex);
    }

    /// <summary>
    /// Returns an enumerator over the key points.
    /// </summary>
    /// <returns>The enumerator.</returns>
    public IEnumerator<CurvePoint<T>> GetEnumerator()
    {
        return _points.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return _points.GetEnumerator();
    }
}
