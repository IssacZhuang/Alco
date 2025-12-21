using System.Runtime.CompilerServices;

using static Alco.math;

namespace Alco;

public static class EasingUtility
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Linear(float t)
    {
        return t;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float QuadIn(float t)
    {
        return t * t;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float QuadOut(float t)
    {
        return t * (2 - t);
    }

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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float CubicIn(float t)
    {
        return t * t * t;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float CubicOut(float t)
    {
        return --t * t * t + 1;
    }

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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float QuartIn(float t)
    {
        return t * t * t * t;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float QuartOut(float t)
    {
        return 1 - (--t * t * t * t);
    }

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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float QuintIn(float t)
    {
        return t * t * t * t * t;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float QuintOut(float t)
    {
        return --t * t * t * t * t + 1;
    }

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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float SineIn(float t)
    {
        return 1 - cos(t * PI / 2);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float SineOut(float t)
    {
        return sin(t * PI / 2);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float SineInOut(float t)
    {
        return 0.5f * (1 - cos(PI * t));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float ExpoIn(float t)
    {
        return t == 0 ? 0 : pow(1024, t - 1);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float ExpoOut(float t)
    {
        return t == 1 ? 1 : 1 - pow(2, -10 * t);
    }

    //[MethodImpl(MethodImplOptions.AggressiveInlining)]
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float CircIn(float t)
    {
        return 1 - sqrt(1 - t * t);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float CircOut(float t)
    {
        return sqrt(1 - (--t * t));
    }

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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float BackIn(float t)
    {
        float s = 1.70158f;
        return t * t * ((s + 1) * t - s);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float BackOut(float t)
    {
        float s = 1.70158f;
        return --t * t * ((s + 1) * t + s) + 1;
    }

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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float BounceIn(float t)
    {
        return 1 - BounceOut(1 - t);
    }

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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float BounceInOut(float t)
    {
        if (t < 0.5f)
        {
            return BounceIn(t * 2) * 0.5f;
        }
        return BounceOut(t * 2 - 1) * 0.5f + 0.5f;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float SmoothStep(float t)
    {
        return t * t * (3 - 2 * t);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float SmootherStep(float t)
    {
        return t * t * t * (t * (t * 6 - 15) + 10);
    }
}