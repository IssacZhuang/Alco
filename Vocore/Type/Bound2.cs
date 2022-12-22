using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.XR;

namespace Vocore
{
    public struct Bound2
    {
        public Vector2 center;
        public Vector2 size;

        public bool Contains(Vector2 point)
        {
            float halfWidth = size.x / 2;
            float halfHeight = size.y / 2;
            return point.x >= center.x - halfWidth && point.x <= center.x + halfWidth &&
                   point.y >= center.y - halfHeight && point.y <= center.y + halfHeight;
        }

        public bool IntersectsCircle(Vector2 position, float radius)
        {
            if (position.x - radius > center.x + size.x / 2)
            {
                return false;
            }

            // Check if the circle is outside the bounding box's right edge
            if (position.x + radius < center.x - size.x / 2)
            {
                return false;
            }

            // Check if the circle is outside the bounding box's top edge
            if (position.y + radius < center.y - size.y / 2)
            {
                return false;
            }

            // Check if the circle is outside the bounding box's bottom edge
            if (position.y - radius > center.y + size.y / 2)
            {
                return false;
            }

            // If none of the above checks returned false, then the bounding box and circle must intersect
            return true;
        }

        public bool ContainedWithinCircle( Vector2 position, float radius)
        {
            // Check if the bounding box's top-left corner is inside the circle
            if (Vector2.Distance(center + new Vector2(-size.x / 2, size.y / 2), position) > radius)
            {
                return false;
            }

            // Check if the bounding box's top-right corner is inside the circle
            if (Vector2.Distance(center + new Vector2(size.x / 2, size.y / 2), position) > radius)
            {
                return false;
            }

            // Check if the bounding box's bottom-left corner is inside the circle
            if (Vector2.Distance(center + new Vector2(-size.x / 2, -size.y / 2), position) > radius)
            {
                return false;
            }

            // Check if the bounding box's bottom-right corner is inside the circle
            if (Vector2.Distance(center + new Vector2(size.x / 2, -size.y / 2), position) > radius)
            {
                return false;
            }

            // If none of the above checks returned false, then the bounding box must be completely inside the circle
            return true;
        }
    }


}
