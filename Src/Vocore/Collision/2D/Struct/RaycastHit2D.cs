using System;
using System.Numerics;

namespace Vocore
{
    public struct RaycastHit2D
    {
        public Vector2 point;
        public Vector2 normal;
        public float fraction;
        public override string ToString()
        {
            return $"point: {point}, normal: {normal}, fraction: {fraction}";
        }
    }
}