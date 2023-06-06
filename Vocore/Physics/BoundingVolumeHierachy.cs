using System;
using System.Collections.Generic;

using Unity.Mathematics;
using Unity.Collections;

namespace Vocore
{
    public unsafe struct BoundingVolumeHierarchy
    {
        public struct Node{
            int left;
            int right;
            ColliderRef collider;
            bool IsLeaf => collider.HasCollider;
        }

        private NativeList<Node> _innerList;
        private NativeList<ColliderRef> _colliderList;

        public void BuildTreeFromColliderList(NativeList<ColliderRef> colliderList)
        {
            _colliderList = colliderList;
            _innerList = new NativeList<Node>(colliderList.Length * 2, Allocator.Temp);
            
        }

        private void BuildTree(int start, int end)
        {
            
        }
    }

}