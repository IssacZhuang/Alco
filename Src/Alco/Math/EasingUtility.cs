using System.Runtime.CompilerServices;

using static Alco.math;

namespace Alco;

/// <summary>
/// Provides easing curves that map normalized time to an easing factor.
/// </summary>
public static class EasingUtility
{
    /// <summary>
    /// Returns the input unchanged, producing linear motion.
    /// </summary>
    /// <param name="t">The normalized time in the range [0, 1].</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Linear(float t)
    {
        return t;
    }

    /// <summary>
    /// Eases in along a quadratic curve.
    /// </summary>
    /// <param name="t">The normalized time in the range [0, 1].</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float QuadIn(float t)
    {
        return t * t;
    }

    /// <summary>
    /// Eases out along a quadratic curve.
    /// </summary>
    /// <param name="t">The normalized time in the range [0, 1].</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float QuadOut(float t)
    {
        return t * (2 - t);
    }

    /// <summary>
    /// Eases in and out along a quadratic curve.
    /// </summary>
    /// <param name="t">The normalized time in the range [0, 1].</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float QuadInOut(float t)
    {
        t *= 2;
        if (t < 1)
        {
            return 0.5f * t * t;
        }
        return -0.5f * (--t * (t - 2) - 1);
    }

    /// <summary>
    /// Eases in along a cubic curve.
    /// </summary>
    /// <param name="t">The normalized time in the range [0, 1].</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float CubicIn(float t)
    {
        return t * t * t;
    }

    /// <summary>
    /// Eases out along a cubic curve.
    /// </summary>
    /// <param name="t">The normalized time in the range [0, 1].</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float CubicOut(float t)
    {
        return --t * t * t + 1;
    }

    /// <summary>
    /// Eases in and out along a cubic curve.
    /// </summary>
    /// <param name="t">The normalized time in the range [0, 1].</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float CubicInOut(float t)
    {
        t *= 2;
        if (t < 1)
        {
            return 0.5f * t * t * t;
        }
        return 0.5f * ((t -= 2) * t * t + 2);
    }

    /// <summary>
    /// Eases in along a quartic curve.
    /// </summary>
    /// <param name="t">The normalized time in the range [0, 1].</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float QuartIn(float t)
    {
        return t * t * t * t;
    }

    /// <summary>
    /// Eases out along a quartic curve.
    /// </summary>
    /// <param name="t">The normalized time in the range [0, 1].</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float QuartOut(float t)
    {
        return 1 - (--t * t * t * t);
    }

    /// <summary>
    /// Eases in and out along a quartic curve.
    /// </summary>
    /// <param name="t">The normalized time in the range [0, 1].</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float QuartInOut(float t)
    {
        t *= 2;
        if (t < 1)
        {
            return 0.5f * t * t * t * t;
        }
        return -0.5f * ((t -= 2) * t * t * t - 2);
    }

    /// <summary>
    /// Eases in along a quintic curve.
    /// </summary>
    /// <param name="t">The normalized time in the range [0, 1].</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float QuintIn(float t)
    {
        return t * t * t * t * t;
    }

    /// <summary>
    /// Eases out along a quintic curve.
    /// </summary>
    /// <param name="t">The normalized time in the range [0, 1].</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float QuintOut(float t)
    {
        return --t * t * t * t * t + 1;
    }

    /// <summary>
    /// Eases in and out along a quintic curve.
    /// </summary>
    /// <param name="t">The normalized time in the range [0, 1].</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float QuintInOut(float t)
    {
        t *= 2;
        if (t < 1)
        {
            return 0.5f * t * t * t * t * t;
        }
        return 0.5f * ((t -= 2) * t * t * t * t + 2);
    }

    /// <summary>
    /// Eases in along a sinusoidal curve.
    /// </summary>
    /// <param name="t">The normalized time in the range [0, 1].</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float SineIn(float t)
    {
        return 1 - cos(t * PI / 2);
    }

    /// <summary>
    /// Eases out along a sinusoidal curve.
    /// </summary>
    /// <param name="t">The normalized time in the range [0, 1].</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float SineOut(float t)
    {
        return sin(t * PI / 2);
    }

    /// <summary>
    /// Eases in and out along a sinusoidal curve.
    /// </summary>
    /// <param name="t">The normalized time in the range [0, 1].</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float SineInOut(float t)
    {
        return 0.5f * (1 - cos(PI * t));
    }

    /// <summary>
    /// Eases in along an exponential curve.
    /// </summary>
    /// <param name="t">The normalized time in the range [0, 1].</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float ExpoIn(float t)
    {
        return t == 0 ? 0 : pow(1024, t - 1);
    }

    /// <summary>
    /// Eases out along an exponential curve.
    /// </summary>
    /// <param name="t">The normalized time in the range [0, 1].</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float ExpoOut(float t)
    {
        return t == 1 ? 1 : 1 - pow(2, -10 * t);
    }

    //[MethodImpl(MethodImplOptions.AggressiveInlining)]
    /// <summary>
    /// Eases in and out along an exponential curve.
    /// </summary>
    /// <param name="t">The normalized time in the range [0, 1].</param>
    public static float ExpoInOut(float t)
    {
        if (t == 0)
        {
            return 0;
        }
        if (t == 1)
        {
            return 1;
        }
        t *= 2;
        if (t < 1)
        {
            return 0.5f * pow(1024, t - 1);
        }
        return 0.5f * (-pow(2, -10 * (t - 1)) + 2);
    }

    /// <summary>
    /// Eases in along a circular curve.
    /// </summary>
    /// <param name="t">The normalized time in the range [0, 1].</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float CircIn(float t)
    {
        return 1 - sqrt(1 - t * t);
    }

    /// <summary>
    /// Eases out along a circular curve.
    /// </summary>
    /// <param name="t">The normalized time in the range [0, 1].</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float CircOut(float t)
    {
        return sqrt(1 - (--t * t));
    }

    /// <summary>
    /// Eases in and out along a circular curve.
    /// </summary>
    /// <param name="t">The normalized time in the range [0, 1].</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float CircInOut(float t)
    {
        t *= 2;
        if (t < 1)
        {
            return -0.5f * (sqrt(1 - t * t) - 1);
        }
        return 0.5f * (sqrt(1 - (t -= 2) * t) + 1);
    }

    //[MethodImpl(MethodImplOptions.AggressiveInlining)]
    /// <summary>
    /// Eases in with an elastic overshoot.
    /// </summary>
    /// <param name="t">The normalized time in the range [0, 1].</param>
    public static float ElasticIn(float t)
    {
        float s;
        float a = 0.1f;
        float p = 0.4f;
        if (t == 0)
        {
            return 0;
        }
        if (t == 1)
        {
            return 1;
        }
        if ( a < 1)
        {
            a = 1;
            s = p / 4;
        }
        else
        {
            s = p * asin(1 / a) / (2 * PI);
        }
        return -(a * pow(2, 10 * (t -= 1)) * sin((t - s) * (2 * PI) / p));
    }

    //[MethodImpl(MethodImplOptions.AggressiveInlining)]
    /// <summary>
    /// Eases out with an elastic overshoot.
    /// </summary>
    /// <param name="t">The normalized time in the range [0, 1].</param>
    public static float ElasticOut(float t)
    {
        float s;
        float a = 0.1f;
        float p = 0.4f;
        if (t == 0)
        {
            return 0;
        }
        if (t == 1)
        {
            return 1;
        }
        if ( a < 1)
        {
            a = 1;
            s = p / 4;
        }
        else
        {
            s = p * asin(1 / a) / (2 * PI);
        }
        return a * pow(2, -10 * t) * sin((t - s) * (2 * PI) / p) + 1;
    }

    /// <summary>
    /// Eases in with a slight backward pull.
    /// </summary>
    /// <param name="t">The normalized time in the range [0, 1].</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float BackIn(float t)
    {
        float s = 1.70158f;
        return t * t * ((s + 1) * t - s);
    }

    /// <summary>
    /// Eases out with a slight overshoot.
    /// </summary>
    /// <param name="t">The normalized time in the range [0, 1].</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float BackOut(float t)
    {
        float s = 1.70158f;
        return --t * t * ((s + 1) * t + s) + 1;
    }

    /// <summary>
    /// Eases in and out with a backward pull and overshoot.
    /// </summary>
    /// <param name="t">The normalized time in the range [0, 1].</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float BackInOut(float t)
    {
        float s = 1.70158f * 1.525f;
        t *= 2;
        if (t < 1)
        {
            return 0.5f * (t * t * ((s + 1) * t - s));
        }
        return 0.5f * ((t -= 2) * t * ((s + 1) * t + s) + 2);
    }

    /// <summary>
    /// Eases in with a bounce effect.
    /// </summary>
    /// <param name="t">The normalized time in the range [0, 1].</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float BounceIn(float t)
    {
        return 1 - BounceOut(1 - t);
    }

    /// <summary>
    /// Eases out with a bounce effect.
    /// </summary>
    /// <param name="t">The normalized time in the range [0, 1].</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float BounceOut(float t)
    {
        if (t < 1 / 2.75f)
        {
            return 7.5625f * t * t;
        }
        if (t < 2 / 2.75f)
        {
            return 7.5625f * (t -= 1.5f / 2.75f) * t + 0.75f;
        }
        if (t < 2.5 / 2.75)
        {
            return 7.5625f * (t -= 2.25f / 2.75f) * t + 0.9375f;
        }
        return 7.5625f * (t -= 2.625f / 2.75f) * t + 0.984375f;
    }

    /// <summary>
    /// Eases in and out with a bounce effect.
    /// </summary>
    /// <param name="t">The normalized time in the range [0, 1].</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float BounceInOut(float t)
    {
        if (t < 0.5f)
        {
            return BounceIn(t * 2) * 0.5f;
        }
        return BounceOut(t * 2 - 1) * 0.5f + 0.5f;
    }

    /// <summary>
    /// Eases with a smooth Hermite interpolation.
    /// </summary>
    /// <param name="t">The normalized time in the range [0, 1].</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float SmoothStep(float t)
    {
        return t * t * (3 - 2 * t);
    }

    /// <summary>
    /// Eases with a smoother Hermite interpolation.
    /// </summary>
    /// <param name="t">The normalized time in the range [0, 1].</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float SmootherStep(float t)
    {
        return t * t * t * (t * (t * 6 - 15) + 10);
    }
}