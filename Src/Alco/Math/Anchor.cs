using System.Numerics;
using System.Runtime.CompilerServices;

namespace Alco;

public struct Anchor
{
    public Vector2 min;
    public Vector2 max;

    public Vector2 CenterPoint
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => min + (max - min) * 0.5f;
    }

    public static readonly Anchor Center = new(new Vector2(0f, 0f), new Vector2(0f, 0f));
    public static readonly Anchor LeftCenter = new(new Vector2(-0.5f, 0f), new Vector2(-0.5f, 0f));
    public static readonly Anchor RightCenter = new(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f));
    public static readonly Anchor CenterTop = new(new Vector2(0f, 0.5f), new Vector2(0f, 0.5f));    
    public static readonly Anchor CenterBottom = new(new Vector2(0f, -0.5f), new Vector2(0f, -0.5f));

    public static readonly Anchor LeftTop = new(new Vector2(-0.5f, 0.5f), new Vector2(-0.5f, 0.5f));
    public static readonly Anchor RightTop = new(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
    public static readonly Anchor LeftBottom = new(new Vector2(-0.5f, -0.5f), new Vector2(-0.5f, -0.5f));
    public static readonly Anchor RightBottom = new(new Vector2(0.5f, -0.5f), new Vector2(0.5f, -0.5f));

    public static readonly Anchor Stretch = new(new Vector2(-0.5f, -0.5f), new Vector2(0.5f, 0.5f));
    public static readonly Anchor LeftVerticalStretch = new(new Vector2(-0.5f, -0.5f), new Vector2(-0.5f, 0.5f));
    public static readonly Anchor CenterVerticalStretch = new(new Vector2(0f, -0.5f), new Vector2(0f, 0.5f));
    public static readonly Anchor RightVerticalStretch = new(new Vector2(0.5f, -0.5f), new Vector2(0.5f, 0.5f));

    public static readonly Anchor TopHorizontalStretch = new(new Vector2(-0.5f, 0.5f), new Vector2(0.5f, 0.5f));
    public static readonly Anchor CenterHorizontalStretch = new(new Vector2(-0.5f, 0f), new Vector2(0.5f, 0f));
    public static readonly Anchor BottomHorizontalStretch = new(new Vector2(-0.5f, -0.5f), new Vector2(0.5f, -0.5f));

    public Anchor(Vector2 min, Vector2 max)
    {
        this.min = min;
        this.max = max;
    }
}