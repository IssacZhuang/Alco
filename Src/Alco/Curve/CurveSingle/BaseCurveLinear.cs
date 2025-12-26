using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Alco;

public abstract class BaseCurveLinear<T> :ICurve<T>, ICollection<CurvePoint<T>> where T : struct
{
    private readonly List<CurvePoint<T>> _points = new List<CurvePoint<T>>();
    private bool _isSortDirty = false;

    public int Count => _points.Count;

    public bool IsReadOnly => false;

    public BaseCurveLinear()
    {

    }

    public BaseCurveLinear(ReadOnlySpan<CurvePoint<T>> points)
    {
        _points.AddRange(points);
        Sort();
    }

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

    public void SetPoints(ReadOnlySpan<CurvePoint<T>> points)
    {
        _points.Clear();
        _points.AddRange(points);
        Sort();
    }

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
        _points.Sort(static (a, b) => a.Time.CompareTo(b.Time));
        _isSortDirty = false;
    }


    public T Evaluate(float time)
    {
        Sort(); // Ensure sorted before evaluation

        if (_points.Count == 0)
        {
            return default;
        }
        if (time <= _points[0].Time)
        {
            return _points[0].Value;
        }
        if (time >= _points[_points.Count - 1].Time)
        {
            return _points[_points.Count - 1].Value;
        }

        int i = BinarySearchFloor(time);
        CurvePoint<T> keyFrame1 = _points[i];
        CurvePoint<T> keyFrame2 = _points[i + 1];
        float t = (time - keyFrame1.Time) / (keyFrame2.Time - keyFrame1.Time);
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
            if (t < _points[mid].Time)
            {
                high = mid - 1;
            }
            else if (t > _points[mid].Time)
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

    public void Add(CurvePoint<T> item)
    {
        _points.Add(item);
        _isSortDirty = true;
    }

    public bool Remove(CurvePoint<T> item)
    {
        if (_points.Remove(item))
        {
            _isSortDirty = true;
            return true;
        }
        return false;
    }

    public void Clear()
    {
        _points.Clear();
        _isSortDirty = false;
    }

    public bool Contains(CurvePoint<T> item)
    {
        return _points.Contains(item);
    }

    public void CopyTo(CurvePoint<T>[] array, int arrayIndex)
    {
        _points.CopyTo(array, arrayIndex);
    }

    public IEnumerator<CurvePoint<T>> GetEnumerator()
    {
        return _points.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return _points.GetEnumerator();
    }
}
