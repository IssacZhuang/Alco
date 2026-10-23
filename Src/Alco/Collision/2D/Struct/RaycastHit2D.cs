using System;
using System.Numerics;

namespace Alco
{
    /// <summary>
    /// Describes a 2D ray cast contact point, normal, and hit fraction.
    /// </summary>
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