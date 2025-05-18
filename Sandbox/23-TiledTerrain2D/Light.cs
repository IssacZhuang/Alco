using System.Numerics;
using Alco;

public class Light : ILight
{
    public Light(int2 position, Vector4 color)
    {
        Position = position;
        Color = color;
    }

    public int2 Position { get; set; }
    public Vector4 Color { get; set; }
}
