using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

using Unity.Jobs;

using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Vocore
{
    public unsafe struct NativeBVH
    {
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
        private Node _root;


        public RayCastResult CastRay(Ray ray)
        {
            if (_nodeList.Length == 0)
            {
                return RayCastResult.none;
            }

            return CastRay(ref ray, _root);
        }

        public void BuildTree(NativeArrayList<ColliderRef> colliders)
        {
            Init();
            Reset();
            BuildBottomTop(colliders);
        }

        [BurstCompile]
        private RayCastResult CastRay(ref Ray ray, Node node)
        {
            if (!UtilsCollision.RayAABB(ray, node.boundingBox)) return RayCastResult.none;
            if (node.IsLeaf)
            {
                if (node.collider.IntersectRay(ray, out RaycastHit hitInfo))
                {
                    return new RayCastResult
                    {
                        hit = true,
                        hitInfo = hitInfo,
                        collider = node.collider
                    };
                }
                else
                {
                    return RayCastResult.none;
                }

            }

            if (node.left >= 0)
            {
                RayCastResult leftResult = CastRay(ref ray, _nodeList[node.left]);
                if (leftResult.hit)
                {
                    return leftResult;
                }
            }

            if (node.right >= 0)
            {
                RayCastResult rightResult = CastRay(ref ray, _nodeList[node.right]);
                if (rightResult.hit)
                {
                    return rightResult;
                }
            }

            return RayCastResult.none;
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

        [BurstCompile]
        private void BuildBottomTop(NativeArrayList<ColliderRef> colliders)
        {
            if (colliders.Length == 0)
            {
                return;
            }

            if (colliders.Length == 1)
            {
                _nodeList.Add(CreateLeaf(colliders[0]));
                _root = _nodeList[0];
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
                _root = CreateParent(start, start + 1);
                _nodeList.Add(_root);
            }
        }

        private Node CreateParent(int singleChild)
        {
            return new Node
            {
                left = singleChild,
                right = -1,
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
                left = -1,
                right = -1,
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
                    left = -1,
                    right = -1,
                    collider = colliders[i],
                });
            }
            return new JobBuildLeaf
            {
                nodeList = (Node*)_nodeList.Ptr,
            }.Schedule(colliders.Length, UtilsJob.GetOptimizedBatchCountByLength(colliders.Length));
        }

        [BurstCompile]
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