using System;
using System.Numerics;

namespace Alco
{
    public struct RaycastHit2D
    {
        public Vector2 Point;
        public Vector2 Normal;
        public float Fraction;
        public override string ToString()
        {
            return $"point: {Point}, normal: {Normal}, fraction: {Fraction}";
        }
    }
}