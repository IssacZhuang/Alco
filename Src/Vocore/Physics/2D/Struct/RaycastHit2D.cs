using System;
using System.Numerics;

namespace Vocore
{
    public struct RaycastHit2D
    {
        public Vector3 point;
        public Vector3 normal;
        public float fraction;
        public override string ToString()
        {
            return $"point: {point}, normal: {normal}, fraction: {fraction}";
        }
    }
}