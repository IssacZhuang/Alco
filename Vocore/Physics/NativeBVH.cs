using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

using Unity.Jobs;
using Unity.Burst;
using Unity.Mathematics;
using Unity.Collections.LowLevel.Unsafe;

namespace Vocore
{
    public unsafe struct NativeBVH : IDisposable
    {
        public struct Node
        {
            public int left;
            public int right;
            public BoundingBox boundingBox;
            public ColliderRef collider;
            public bool IsLeaf => collider.HasCollider;
        }

        private NativeBuffer<Node> _nodes;
        private NativeBuffer<RayCastResult> _batchRayCastResult;
        private NativeBuffer<ColliderCastResult> _batchColliderBoxCastResult;
        private NativeBuffer<ColliderCastResult> _batchColliderSphereCastResult;
        private Node _root;
        private int _nodeSize;
        private bool _isDisposed;

        public int Size => _nodeSize;
        public int Capacity => _nodes.Length;

        public RayCastResult CastRay(Ray ray)
        {
            if (_nodeSize == 0)
            {
                return RayCastResult.none;
            }

            return CastRay(ref ray, _root);
        }

        public RayCastResult CastRayFast(Ray ray)
        {
            if (_nodeSize == 0)
            {
                return RayCastResult.none;
            }

            return CastRayFast(ref ray, _root);
        }

        public NativeBuffer<RayCastResult> CastBatchRayFast(NativeArrayList<Ray> rays)
        {
            _batchRayCastResult.FastEnsureSize(rays.Length);
            JobCastRayFast job = new JobCastRayFast
            {
                bvh = this,
                rays = rays.Ptr,
                results = _batchRayCastResult.Ptr
            };
            job.Schedule(rays.Length, UtilsJob.GetOptimizedBatchCountByLength(rays.Length)).Complete();

            return _batchRayCastResult;
        }

        public NativeBuffer<RayCastResult> CastBatchRay(NativeArrayList<Ray> rays)
        {
            _batchRayCastResult.FastEnsureSize(rays.Length);
            JobCastRay job = new JobCastRay
            {
                bvh = this,
                rays = rays.Ptr,
                results = _batchRayCastResult.Ptr
            };
            job.Schedule(rays.Length, UtilsJob.GetOptimizedBatchCountByLength(rays.Length)).Complete();
            return _batchRayCastResult;
        }

        public NativeBuffer<ColliderCastResult> CastBatchColliderBox(NativeArrayList<ColliderBox> colliders)
        {
            _batchColliderBoxCastResult.FastEnsureSize(colliders.Length);
            JobCastColliderBox job = new JobCastColliderBox
            {
                bvh = this,
                colliderBoxes = colliders.Ptr,
                results = _batchColliderBoxCastResult.Ptr
            };
            job.Schedule(colliders.Length, UtilsJob.GetOptimizedBatchCountByLength(colliders.Length)).Complete();
            return _batchColliderBoxCastResult;
        }

        public NativeBuffer<ColliderCastResult> CastBatchColliderSphere(NativeArrayList<ColliderSphere> colliders)
        {
            _batchColliderSphereCastResult.FastEnsureSize(colliders.Length);
            JobCastColliderSphere job = new JobCastColliderSphere
            {
                bvh = this,
                colliderSpheres = colliders.Ptr,
                results = _batchColliderSphereCastResult.Ptr
            };
            job.Schedule(colliders.Length, UtilsJob.GetOptimizedBatchCountByLength(colliders.Length)).Complete();
            return _batchColliderSphereCastResult;
        }

        public void BuildTree(NativeArrayList<ColliderRef> colliders)
        {
            _nodes.FastEnsureSize(colliders.Length * 2 + (int)math.sqrt(colliders.Length) + 2);
            BuildBottomTop(colliders);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Node GetNodeSafe(int index)
        {
            return _nodes[index];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Node GetNode(int index)
        {
            return _nodes.Ptr[index];
        }

        private RayCastResult CastRayFast(ref Ray ray, Node node)
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
                RayCastResult leftResult = CastRayFast(ref ray, GetNode(node.left));
                if (leftResult.hit)
                {
                    return leftResult;
                }
            }

            if (node.right >= 0)
            {
                RayCastResult rightResult = CastRayFast(ref ray, GetNode(node.right));
                if (rightResult.hit)
                {
                    return rightResult;
                }
            }

            return RayCastResult.none;
        }

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

            RayCastResult leftResult = RayCastResult.Default;
            RayCastResult rightResult = RayCastResult.Default;

            if (node.left >= 0)
            {
                leftResult = CastRayFast(ref ray, GetNode(node.left));
            }

            if (node.right >= 0)
            {
                rightResult = CastRayFast(ref ray, GetNode(node.right));
            }

            if (leftResult.hit == true && rightResult.hit == true)
            {
                if (leftResult.hitInfo.fraction < rightResult.hitInfo.fraction)
                {
                    return leftResult;
                }
                else
                {
                    return rightResult;
                }
            }

            if (leftResult.hit != rightResult.hit)
            {
                if (leftResult.hit)
                {
                    return leftResult;
                }
                else
                {
                    return rightResult;
                }
            }

            return RayCastResult.none;
        }

        public ColliderCastResult CastColliderBox(ref ColliderBox colliderBox)
        {
            if (_nodeSize == 0)
            {
                return ColliderCastResult.None;
            }

            return CastColliderBox(ref colliderBox, _root);
        
        }

        private ColliderCastResult CastColliderBox(ref ColliderBox colliderBox, Node node)
        {
            if (!colliderBox.GetBoundingBox().Intersects(node.boundingBox)) return ColliderCastResult.None;

            if (node.IsLeaf)
            {
                if (node.collider.CollidesWith<ColliderBox>(colliderBox))
                {
                    return new ColliderCastResult
                    {
                        hit = true,
                        collider = node.collider
                    };
                }
                else
                {
                    return ColliderCastResult.None;
                }

            }

            if (node.left >= 0)
            {
                ColliderCastResult leftResult = CastColliderBox(ref colliderBox, GetNode(node.left));
                if (leftResult.hit)
                {
                    return leftResult;
                }
            }

            if (node.right >= 0)
            {
                ColliderCastResult rightResult = CastColliderBox(ref colliderBox, GetNode(node.right));
                if (rightResult.hit)
                {
                    return rightResult;
                }
            }

            return ColliderCastResult.None;
        }

        public ColliderCastResult CastColliderSphere(ref ColliderSphere colliderSphere)
        {
            if (_nodeSize == 0)
            {
                return ColliderCastResult.None;
            }

            return CastColliderSphere(ref colliderSphere, _root);
        }

        private ColliderCastResult CastColliderSphere(ref ColliderSphere colliderSphere, Node node)
        {
            if (!colliderSphere.GetBoundingBox().Intersects(node.boundingBox)) return ColliderCastResult.None;

            if (node.IsLeaf)
            {
                if (node.collider.CollidesWith<ColliderSphere>(colliderSphere))
                {
                    return new ColliderCastResult
                    {
                        hit = true,
                        collider = node.collider
                    };
                }
                else
                {
                    return ColliderCastResult.None;
                }

            }

            if (node.left >= 0)
            {
                ColliderCastResult leftResult = CastColliderSphere(ref colliderSphere, GetNode(node.left));
                if (leftResult.hit)
                {
                    return leftResult;
                }
            }

            if (node.right >= 0)
            {
                ColliderCastResult rightResult = CastColliderSphere(ref colliderSphere, GetNode(node.right));
                if (rightResult.hit)
                {
                    return rightResult;
                }
            }

            return ColliderCastResult.None;
        }


        private void BuildBottomTop(NativeArrayList<ColliderRef> colliders)
        {
            _nodeSize = 0;

            if (colliders.Length == 0)
            {
                return;
            }

            if (colliders.Length == 1)
            {
                AddNode(CreateLeaf(colliders[0]));
                _root = _nodes[0];
                return;
            }

            StartJobBuildLeaf(colliders).Complete();
            BuildBranch();
        }

        private void BuildBranch()
        {
            int start = 0;
            int end = _nodeSize;

            Node* ptr = _nodes.Ptr;

            while (start < end - 2)
            {
                int parentCount = (end - start + 1) / 2;
                for (int i = 0; i < parentCount; i++)
                {
                    int left = start + i * 2;
                    int right = start + i * 2 + 1;

                    if (right >= end)
                    {
                        ptr[end + i] = CreateParent(left);
                    }
                    else
                    {
                        ptr[end + i] = CreateParent(left, right);
                    }
                }

                start = end;
                end = start + parentCount;
                _nodeSize += parentCount;
            }

            if (end - start == 2)
            {
                _root = CreateParent(start, start + 1);
                AddNode(_root);
            }
        }

        private Node CreateParent(int singleChild)
        {
            return new Node
            {
                left = singleChild,
                right = -1,
                boundingBox = GetNode(singleChild).boundingBox
            };
        }

        private Node CreateParent(int left, int right)
        {
            return new Node
            {
                left = left,
                right = right,
                boundingBox = BoundingBox.Merge(GetNode(left).boundingBox, GetNode(right).boundingBox)
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
            Node* ptr = _nodes.Ptr;

            for (int i = 0; i < colliders.Length; i++)
            {
                ptr[i].left = -1;
                ptr[i].right = -1;
                ptr[i].collider = colliders[i];
            }

            _nodeSize = colliders.Length;

            return new JobBuildLeaf
            {
                nodeList = _nodes.Ptr,
            }.Schedule(colliders.Length, UtilsJob.GetOptimizedBatchCountByLength(colliders.Length));
        }

        private JobHandle StartJobBuildBranch()
        {
            return new JobBuildBranch
            {
                bvh = this
            }.Schedule();
        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            _batchRayCastResult.Dispose();
            _nodes.Dispose();
            _isDisposed = true;
        }

        private void AddNode(Node node)
        {
            if (_nodeSize >= _nodes.Length)
            {
                return;
            }
            _nodes.Ptr[_nodeSize] = node;
            _nodeSize++;
        }

        [BurstCompile]
        private struct JobBuildBranch : IJob
        {
            public NativeBVH bvh;
            public void Execute()
            {
                bvh.BuildBranch();
            }
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

        [BurstCompile]
        private struct JobCastRay : IJobParallelFor
        {
            public NativeBVH bvh;
            [NativeDisableUnsafePtrRestriction]
            public Ray* rays;
            [NativeDisableUnsafePtrRestriction]
            public RayCastResult* results;

            public void Execute(int index)
            {
                results[index] = bvh.CastRay(rays[index]);
            }
        }

        [BurstCompile]
        private struct JobCastRayFast : IJobParallelFor
        {
            public NativeBVH bvh;
            [NativeDisableUnsafePtrRestriction]
            public Ray* rays;
            [NativeDisableUnsafePtrRestriction]
            public RayCastResult* results;

            public void Execute(int index)
            {
                results[index] = bvh.CastRayFast(rays[index]);
            }
        }

        [BurstCompile]
        private struct JobCastColliderBox : IJobParallelFor
        {
            public NativeBVH bvh;
            [NativeDisableUnsafePtrRestriction]
            public ColliderBox* colliderBoxes;
            [NativeDisableUnsafePtrRestriction]
            public ColliderCastResult* results;

            public void Execute(int index)
            {
                results[index] = bvh.CastColliderBox(ref colliderBoxes[index]);
            }
        }

        [BurstCompile]
        private struct JobCastColliderSphere : IJobParallelFor
        {
            public NativeBVH bvh;
            [NativeDisableUnsafePtrRestriction]
            public ColliderSphere* colliderSpheres;
            [NativeDisableUnsafePtrRestriction]
            public ColliderCastResult* results;

            public void Execute(int index)
            {
                results[index] = bvh.CastColliderSphere(ref colliderSpheres[index]);
            }
        }
    }
}