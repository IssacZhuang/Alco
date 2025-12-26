using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace Alco;

/// <summary>
/// Base class for cached curves that samples an existing curve at fixed intervals for O(1) evaluation.
/// </summary>
/// <typeparam name="T">The value type of the curve.</typeparam>
public abstract class BaseCurveCache<T> : ICurve<T> where T : struct
{
    public const float DefaultStep = 1 / 60f;

    private readonly CurvePoint<T>[] _points;
    private readonly float _step;
    private readonly float _startTime;
    private readonly float _endTime;

    /// <summary>
    /// Gets the number of points in the cache.
    /// </summary>
    public int Count => _points.Length;

    /// <summary>
    /// Gets the list of cached points.
    /// </summary>
    public IReadOnlyList<CurvePoint<T>> Points => _points;

    /// <summary>
    /// Gets the point at the specified index.
    /// </summary>
    /// <param name="index">The index of the point.</param>
    public CurvePoint<T> this[int index] => _points[index];

    /// <summary>
    /// Initializes a new instance of the <see cref="BaseCurveCache{T}"/> class.
    /// </summary>
    /// <param name="curve">The source curve to cache.</param>
    /// <param name="step">The sampling step size.</param>
    public BaseCurveCache(ICurve<T> curve, float step = DefaultStep)
    {
        ArgumentNullException.ThrowIfNull(curve);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(step);

        if (curve.Count == 0)
        {
            _points = Array.Empty<CurvePoint<T>>();
            _step = step;
            _startTime = 0;
            _endTime = 0;
            return;
        }

        _step = step;
        _startTime = curve[0].Time;
        _endTime = curve[curve.Count - 1].Time;
        _points = CacheCurve(curve, step);
    }

    /// <summary>
    /// Evaluates the curve at the specified time using linear interpolation between cached points.
    /// </summary>
    /// <param name="t">The time to evaluate at.</param>
    /// <returns>The interpolated value.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T Evaluate(float t)
    {
        if (_points.Length == 0)
        {
            return default;
        }

        t = math.clamp(t, _startTime, _endTime);
        
        // Find the index directly using floor division
        int index = (int)math.floor((t - _startTime) / _step);
        
        // Ensure index is within bounds (handle precision issues or end time case)
        if (index >= _points.Length - 1)
        {
             return _points[_points.Length - 1].Value;
        }
        if (index < 0)
        {
            return _points[0].Value;
        }

        int index2 = index + 1;
        
        float t1 = _points[index].Time;
        float t2 = _points[index2].Time; // Should be equivalent to t1 + _step, but use actual point time for precision
        
        float vT = (t - t1) / (t2 - t1);
        
        return Lerp(_points[index].Value, _points[index2].Value, vT);
    }

    private CurvePoint<T>[] CacheCurve(ICurve<T> curve, float step)
    {
        float duration = _endTime - _startTime;
        int count = (int)math.floor(duration / step) + 2;

        CurvePoint<T>[] points = new CurvePoint<T>[count];
        
        // Use Parallel.For for faster sampling of complex curves
        Parallel.For(0, count - 1, (i) =>
        {
            float t = _startTime + i * step;
            // Clamp t to ensure we don't exceed end time due to float precision
            if (t > _endTime) t = _endTime;
            
            T value = curve.Evaluate(t);
            points[i] = new CurvePoint<T>(t, value);
        });

        // Ensure the last point is exactly the end time and value
        points[count - 1] = new CurvePoint<T>(_endTime, curve.Evaluate(_endTime));

        return points;
    }

    /// <summary>
    /// Linearly interpolates between two values.
    /// </summary>
    /// <param name="a">The start value.</param>
    /// <param name="b">The end value.</param>
    /// <param name="t">The interpolation factor (0-1).</param>
    /// <returns>The interpolated value.</returns>
    protected abstract T Lerp(T a, T b, float t);
}

