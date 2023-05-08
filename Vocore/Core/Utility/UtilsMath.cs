using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

using UnityEngine;




namespace Vocore
{
    public static class UtilsMath
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 Transform(Vector3 transformFrom, Quaternion rotationFrom, Vector3 transformTo, Quaternion rotationTo, Vector3 point)
        {
            return rotationTo * (rotationFrom * point + transformFrom - transformTo);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 Transform(Vector3 transformFrom, Quaternion rotationFrom, Vector3 point)
        {
            return rotationFrom.Invert() * point + transformFrom;
        }

    }
}

