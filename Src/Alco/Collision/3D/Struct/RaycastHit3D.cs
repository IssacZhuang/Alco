using System;
using System.Numerics;
using System.Collections.Generic;



namespace Alco
{
    /// <summary>
    /// Describes a 3D ray cast contact point, normal, and hit fraction.
    /// </summary>
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

