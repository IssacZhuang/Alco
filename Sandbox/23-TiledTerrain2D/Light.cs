using System.Numerics;
using Alco;

/// <summary>A point light positioned on the tile grid.</summary>
public class Light : ILight
{
    /// <summary>Initializes a light at the given grid position and color.</summary>
    /// <param name="position">Grid position of the light.</param>
    /// <param name="color">Light color and intensity.</param>
    public Light(int2 position, Vector4 color)
    {
        Position = position;
        Color = color;
    }

    /// <summary>Grid position of the light.</summary>
    public int2 Position { get; set; }
    /// <summary>Light color and intensity.</summary>
    public Vector4 Color { get; set; }
}
