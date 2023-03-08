using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using UnityEngine;

namespace Vocore
{
    public static class UtilsVector
    {
        /// <summary>
        /// Projects a Vector 3 to a plane face to axis X.
        /// </summary>
        public static Vector2 ToPlaneX(this Vector3 from)
        {
            Vector2 result = default;
            result.Set(from.y, from.z);
            return result;
        }

        /// <summary>
        /// Projects a Vector 3 to a plane face to axis Y.
        /// </summary>
        public static Vector2 ToPlaneY(this Vector3 from)
        {
            Vector2 result = default;
            result.Set(from.x, from.z);
            return result;
        }

        /// <summary>
        /// Projects a Vector 3 to a plane face to axis Y.
        /// </summary>
        public static Vector2 ToPlaneZ(this Vector3 from)
        {
            Vector2 result = default;
            result.Set(from.x, from.y);
            return result;
        }

        /// <summary>
        /// Transfer a Vector2 in plane that face to axis X to 3D space.
        /// </summary>
        public static Vector3 FromPlaneX(this Vector2 from)
        {
            Vector3 result = default;
            result.Set(0, from.x, from.y);
            return result;
        }

        /// <summary>
        /// Transfer a Vector2 in plane that face to axis Y to 3D space.
        /// </summary>
        public static Vector3 FromPlaneY(this Vector2 from)
        {
            Vector3 result = default;
            result.Set(from.x, 0, from.y);
            return result;
        }

        /// <summary>
        /// Transfer a Vector2 in plane that face to axis Y to 3D space.
        /// </summary>
        public static Vector3 FromPlaneZ(this Vector2 from)
        {
            Vector3 result = default;
            result.Set(from.x, from.y, 0);
            return result;
        }
    }
}
