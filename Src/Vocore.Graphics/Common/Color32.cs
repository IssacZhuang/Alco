using System.Runtime.InteropServices;

namespace Vocore.Graphics;

[StructLayout(LayoutKind.Sequential)]
public struct Color32
{
    public byte R;
    public byte G;
    public byte B;
    public byte A;

    public Color32(byte r, byte g, byte b, byte a)
    {
        R = r;
        G = g;
        B = b;
        A = a;
    }

    public Color32(byte r, byte g, byte b)
    {
        R = r;
        G = g;
        B = b;
        A = 255;
    }
}