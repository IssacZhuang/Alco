using System;
using System.Collections.Generic;

using Unity.Mathematics;

namespace Vocore
{
    public unsafe struct BoundingVolumeHierarchy
    {
        public struct Node{
            bool isLeaf;
            int left;
            int right;
            int colliderRefIndex;
        }

        private NativeList<Node> _innerList;
        private NativeList<ColliderRef> _colliderList;
    }

}