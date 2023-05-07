using System;
using System.Collections.Generic;

using UnityEngine;

namespace Vocore
{
    public struct BoundingBox
    {
        public Vector3 AA;
        public Vector3 BB;

        public BoundingBox(Vector3 aa, Vector3 bb)
        {
            AA = aa;
            BB = bb;
        }

        public bool Contains(Vector3 point)
        {
            return point.x >= AA.x && point.x <= BB.x &&
                   point.y >= AA.y && point.y <= BB.y &&
                   point.z >= AA.z && point.z <= BB.z;
        }

        public bool Contains(BoundingBox other)
        {
            return other.AA.x >= AA.x && other.BB.x <= BB.x &&
                   other.AA.y >= AA.y && other.BB.y <= BB.y &&
                   other.AA.z >= AA.z && other.BB.z <= BB.z;
        }

        public bool Intersects(BoundingBox other)
        {
            return AA.x <= other.BB.x && BB.x >= other.AA.x &&
                   AA.y <= other.BB.y && BB.y >= other.AA.y &&
                   AA.z <= other.BB.z && BB.z >= other.AA.z;
        }
    }
}