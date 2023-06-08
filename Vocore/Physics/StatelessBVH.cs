using System;
using System.Collections.Generic;

using Unity.Jobs;

using UnityEngine;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Vocore
{
    public unsafe struct StatelessBVH
    {
        public enum Algorithm
        {
            SortX,
            SortY,
            SortZ,
        }

        public struct Node
        {
            public int left;
            public int right;
            public BoundingBox boundingBox;
            public ColliderRef collider;
            public bool IsLeaf => collider.HasCollider;
        }

        private NativeArrayList<Node> _nodeList;
        //private NativeArrayList<ColliderRef> _colliderList;
        private bool _initialized;

        public void BuildTree(NativeArrayList<ColliderRef> colliders)
        {
            Init();
            Reset();
            BuildBottomTop(colliders);
        }

        private void Init()
        {
            if (_initialized)
            {
                return;
            }

            _initialized = true;
            _nodeList = new NativeArrayList<Node>(64, false);
        }

        private void Reset()
        {
            _nodeList.Clear();
        }

        private void BuildBottomTop(NativeArrayList<ColliderRef> colliders)
        {
            if (colliders.Length == 0)
            {
                return;
            }

            if (colliders.Length == 1)
            {
                _nodeList.Add(CreateLeaf(colliders[0]));
                return;
            }

            // for (int i = 0; i < _colliderList.Length; i++)
            // {
            //     _nodeList.Add(CreateLeaf(_colliderList[i]));
            // }

            StartJobBuildLeaf(colliders).Complete();

            int start = 0;
            int end = _nodeList.Length;

            while (start < end - 2)
            {
                int parentCount = (end - start + 1) / 2;
                for (int i = start; i < end; i += 2)
                {
                    int left = i;
                    int right = i + 1;
                    if (right >= end)
                    {
                        _nodeList.Add(CreateParent(left));
                    }
                    else
                    {
                        _nodeList.Add(CreateParent(left, right));
                    }
                }

                start = end;
                end = start + parentCount;
            }

            if (end - start == 2)
            {
                _nodeList.Add(CreateParent(start, start + 1));
            }
        }

        private Node CreateParent(int singleChild)
        {
            return new Node
            {
                left = singleChild,
                right = singleChild,
                boundingBox = _nodeList[singleChild].boundingBox
            };
        }

        private Node CreateParent(int left, int right)
        {
            return new Node
            {
                left = left,
                right = right,
                boundingBox = BoundingBox.Merge(_nodeList[left].boundingBox, _nodeList[right].boundingBox)
            };
        }

        private Node CreateLeaf(ColliderRef collider)
        {
            return new Node
            {
                collider = collider,
                boundingBox = collider.GetBoundingBox(),
            };
        }

        private JobHandle StartJobBuildLeaf(NativeArrayList<ColliderRef> colliders)
        {
            for (int i = 0; i < colliders.Length; i++)
            {
                _nodeList.Add(new Node
                {
                    collider = colliders[i],
                });
            }
            return new JobBuildLeaf
            {
                nodeList = (Node*)_nodeList.Ptr,
            }.Schedule(colliders.Length, Environment.ProcessorCount * 2);
        }

        private struct JobBuildLeaf : IJobParallelFor
        {
            [NativeDisableUnsafePtrRestriction]
            public Node* nodeList;

            public void Execute(int index)
            {
                nodeList[index].boundingBox = nodeList[index].collider.GetBoundingBox();
            }
        }


    }

}