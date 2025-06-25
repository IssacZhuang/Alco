
using System.Numerics;

namespace Alco.Graphics;

public struct ClearColorData
{
    public ClearColorData(uint index, Vector4 color)
    {
        Color = color;
        Index = index;
    }

    public Vector4 Color;
    public uint Index;
}