using System.Runtime.CompilerServices;

namespace Alco.GUI;

public struct TextAlign
{
    public float value;

    public TextAlign(float value)
    {
        this.value = value;
    }

    public static readonly TextAlign Left = new(-0.5f);
    public static readonly TextAlign Center = new(0);
    public static readonly TextAlign Right = new(0.5f);
    public static readonly TextAlign Top = new(0.5f);
    public static readonly TextAlign Bottom = new(-0.5f);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator TextAlign(float v) => new(v);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator float(TextAlign p) => p.value;
}
