using System;
using System.Numerics;
using System.Collections.Generic;



namespace Alco
{
    public struct RaycastHit3D
    {
        public Vector3 Point;
        public Vector3 Normal;
        public float Fraction;

        public override string ToString()
        {
            return $"point: {Point}, normal: {Normal}";
        }
    }
}

