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
            int colliderIndex;
        }

        private NativeList<Node> _innerList;
        private void* _colliderList;
    }

}