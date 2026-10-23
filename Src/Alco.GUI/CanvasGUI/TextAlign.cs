using System.Runtime.CompilerServices;

namespace Alco.GUI;

/// <summary>
/// Represents a normalized text alignment value along one axis, where -0.5 is the low edge and 0.5 is the high edge.
/// </summary>
public struct TextAlign
{
    public float value;

    /// <summary>Creates an alignment with the specified normalized value.</summary>
    /// <param name="value">The normalized alignment value.</param>
    public TextAlign(float value)
    {
        this.value = value;
    }

    /// <summary>Aligns text to the left edge.</summary>
    public static readonly TextAlign Left = new(-0.5f);
    /// <summary>Aligns text to the center.</summary>
    public static readonly TextAlign Center = new(0);
    /// <summary>Aligns text to the right edge.</summary>
    public static readonly TextAlign Right = new(0.5f);
    /// <summary>Aligns text to the top edge.</summary>
    public static readonly TextAlign Top = new(0.5f);
    /// <summary>Aligns text to the bottom edge.</summary>
    public static readonly TextAlign Bottom = new(-0.5f);

    /// <summary>Converts a normalized float to a <see cref="TextAlign"/>.</summary>
    /// <param name="v">The normalized alignment value.</param>
    /// <returns>The corresponding alignment.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator TextAlign(float v) => new(v);
    /// <summary>Converts a <see cref="TextAlign"/> to its normalized float value.</summary>
    /// <param name="p">The alignment to convert.</param>
    /// <returns>The normalized alignment value.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator float(TextAlign p) => p.value;
}
