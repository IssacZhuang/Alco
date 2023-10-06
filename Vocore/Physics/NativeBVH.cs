using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;


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

            return CastRayOptimized(ref ray, _root);
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
            job.RunParallel(rays.Length);

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
            job.RunParallel(rays.Length);
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
            job.RunParallel(colliders.Length);
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
            job.RunParallel(colliders.Length);
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
            //NativeStack<Node> stack = new NativeStack<Node>(_nodeSize * 2);
            Node* stack = stackalloc Node[_nodeSize / 2 + 2];
            int stackCount = 0;
            stack[stackCount++] = node;
            RayCastResult result = RayCastResult.none;


            while (stackCount > 0)
            {
                //Node top = stack.Pop();
                Node top = stack[--stackCount];

                if (!UtilsCollision.RayAABB(ray, top.boundingBox)) continue;

                if (top.IsLeaf)
                {
                    if (top.collider.IntersectRay(ray, out RaycastHit hitInfo))
                    {
                        result.hit = true;
                        result.hitInfo = hitInfo;
                        result.collider = top.collider;
                        return result;
                    }

                    continue;

                }

                if (top.left >= 0)
                {
                    stack[stackCount++] = GetNode(top.left);
                }

                if (top.right >= 0)
                {
                    stack[stackCount++] = GetNode(top.right);
                }

            }

            return result;
        }

        private RayCastResult CastRayOptimized(ref Ray ray, Node node)
        {
            //NativeStack<Node> stack = new NativeStack<Node>(_nodeSize * 2);
            Node* stack = stackalloc Node[_nodeSize / 2 + 2];
            int stackCount = 0;
            stack[stackCount++] = node;
            RayCastResult result = RayCastResult.none;


            while (stackCount > 0)
            {
                //Node top = stack.Pop();
                Node top = stack[--stackCount];

                if (!UtilsCollision.RayAABB(ray, top.boundingBox)) continue;

                if (top.IsLeaf)
                {
                    if (top.collider.IntersectRay(ray, out RaycastHit hitInfo))
                    {  
                        if (!result.hit ||result.hit && hitInfo.fraction < result.hitInfo.fraction)
                        {
                            result.hit = true;
                            result.hitInfo = hitInfo;
                            result.collider = top.collider;
                        }
                    }

                    continue;

                }

                if (top.left >= 0)
                {
                    stack[stackCount++] = GetNode(top.left);
                }

                if (top.right >= 0)
                {
                    stack[stackCount++] = GetNode(top.right);
                }

            }

            return result;
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

            StartJobBuildLeaf(colliders);
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

        private void StartJobBuildLeaf(NativeArrayList<ColliderRef> colliders)
        {
            Node* ptr = _nodes.Ptr;

            for (int i = 0; i < colliders.Length; i++)
            {
                ptr[i].left = -1;
                ptr[i].right = -1;
                ptr[i].collider = colliders[i];
                ptr[i].boundingBox = colliders[i].GetBoundingBox();
            }

            _nodeSize = colliders.Length;

            // new JobBuildLeaf
            // {
            //     nodeList = _nodes.Ptr,
            // }.Run(colliders.Length);
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

        private struct JobCastRay : IJobBatch
        {
            public NativeBVH bvh;
            public Ray* rays;
            public RayCastResult* results;
            public void Execute(int index)
            {
                results[index] = bvh.CastRay(rays[index]);
            }
        }

        private struct JobCastRayFast : IJobBatch
        {
            public NativeBVH bvh;
            public Ray* rays;
            public RayCastResult* results;

            public void Execute(int index)
            {
                results[index] = bvh.CastRayFast(rays[index]);
            }
        }

        private struct JobCastColliderBox : IJobBatch
        {
            public NativeBVH bvh;
            public ColliderBox* colliderBoxes;
            public ColliderCastResult* results;

            public void Execute(int index)
            {
                results[index] = bvh.CastColliderBox(ref colliderBoxes[index]);
            }
        }

        private struct JobCastColliderSphere : IJobBatch
        {
            public NativeBVH bvh;
            public ColliderSphere* colliderSpheres;
            public ColliderCastResult* results;

            public void Execute(int index)
            {
                results[index] = bvh.CastColliderSphere(ref colliderSpheres[index]);
            }
        }
    }
}