using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;


namespace Vocore
{
    public unsafe struct NativeBvh2D : IDisposable
    {


        public struct Node
        {
            public int left;
            public int right;
            public BoundingBox2D boundingBox;
            public ColliderRef2D collider;
            public bool IsLeaf => collider.HasCollider;
        }

        private NativeBuffer<Node> _nodes;
        private NativeBuffer<RayCastResult2D> _batchRayCastResult;
        private NativeBuffer<ColliderCastResult2D> _batchColliderBoxCastResult;
        private NativeBuffer<ColliderCastResult2D> _batchColliderSphereCastResult;
        private Node _root;
        private int _nodeSize;
        private bool _isDisposed;

        public int Size => _nodeSize;
        public int Capacity => _nodes.Length;

        public RayCastResult2D CastRay(Ray2D ray)
        {
            if (_nodeSize == 0)
            {
                return RayCastResult2D.none;
            }

            return CastRayOptimized(ref ray, _root);
        }

        public RayCastResult2D CastRayFast(Ray2D ray)
        {
            if (_nodeSize == 0)
            {
                return RayCastResult2D.none;
            }

            return CastRayFast(ref ray, _root);
        }

        public NativeBuffer<RayCastResult2D> CastBatchRayFast(NativeArrayList<Ray2D> rays)
        {
            _batchRayCastResult.EnsureSizeNoCopy(rays.Length);
            JobCastRayFast job = new JobCastRayFast
            {
                bvh = this,
                rays = rays.DataPtr,
                results = _batchRayCastResult.DataPtr
            };
            job.RunParallel(rays.Length);

            return _batchRayCastResult;
        }

        public NativeBuffer<RayCastResult2D> CastBatchRay(NativeArrayList<Ray2D> rays)
        {
            _batchRayCastResult.EnsureSizeNoCopy(rays.Length);
            JobCastRay job = new JobCastRay
            {
                bvh = this,
                rays = rays.DataPtr,
                results = _batchRayCastResult.DataPtr
            };
            job.RunParallel(rays.Length);
            return _batchRayCastResult;
        }

        public NativeBuffer<ColliderCastResult2D> CastBatchColliderBox(NativeArrayList<ColliderBox2D> colliders)
        {
            _batchColliderBoxCastResult.EnsureSizeNoCopy(colliders.Length);
            JobCastColliderBox job = new JobCastColliderBox
            {
                bvh = this,
                colliderBoxes = colliders.DataPtr,
                results = _batchColliderBoxCastResult.DataPtr
            };
            job.RunParallel(colliders.Length);
            return _batchColliderBoxCastResult;
        }

        public NativeBuffer<ColliderCastResult2D> CastBatchColliderSphere(NativeArrayList<ColliderSphere2D> colliders)
        {
            _batchColliderSphereCastResult.EnsureSizeNoCopy(colliders.Length);
            JobCastColliderSphere job = new JobCastColliderSphere
            {
                bvh = this,
                colliderSpheres = colliders.DataPtr,
                results = _batchColliderSphereCastResult.DataPtr
            };
            job.RunParallel(colliders.Length);
            return _batchColliderSphereCastResult;
        }

        public void BuildTree(NativeArrayList<ColliderRef2D> colliders)
        {
            _nodes.EnsureSizeNoCopy(colliders.Length * 2 + (int)math.sqrt(colliders.Length) + 2);
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
            return _nodes.DataPtr[index];
        }

        private RayCastResult2D CastRayFast(ref Ray2D ray, Node node)
        {
            //NativeStack<Node> stack = new NativeStack<Node>(_nodeSize * 2);
            Node* stack = stackalloc Node[_nodeSize / 2 + 2];
            int stackCount = 0;
            stack[stackCount++] = node;
            RayCastResult2D result = RayCastResult2D.none;


            while (stackCount > 0)
            {
                //Node top = stack.Pop();
                Node top = stack[--stackCount];

                if (!UtilsCollision2D.RayAABB(ray, top.boundingBox)) continue;

                if (top.IsLeaf)
                {
                    if (top.collider.IntersectRay(ray, out RaycastHit2D hitInfo))
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

        private RayCastResult2D CastRayOptimized(ref Ray2D ray, Node node)
        {
            //NativeStack<Node> stack = new NativeStack<Node>(_nodeSize * 2);
            Node* stack = stackalloc Node[_nodeSize / 2 + 2];
            int stackCount = 0;
            stack[stackCount++] = node;
            RayCastResult2D result = RayCastResult2D.none;


            while (stackCount > 0)
            {
                //Node top = stack.Pop();
                Node top = stack[--stackCount];

                if (!UtilsCollision2D.RayAABB(ray, top.boundingBox)) continue;

                if (top.IsLeaf)
                {
                    if (top.collider.IntersectRay(ray, out RaycastHit2D hitInfo))
                    {
                        if (!result.hit || result.hit && hitInfo.fraction < result.hitInfo.fraction)
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

        public ColliderCastResult2D CastColliderBox(ref ColliderBox2D colliderBox)
        {
            if (_nodeSize == 0)
            {
                return ColliderCastResult2D.None;
            }

            return CastColliderBox(ref colliderBox, _root);

        }

        private ColliderCastResult2D CastColliderBox(ref ColliderBox2D colliderBox, Node node)
        {
            if (!colliderBox.GetBoundingBox().Intersects(node.boundingBox)) return ColliderCastResult2D.None;

            if (node.IsLeaf)
            {
                if (node.collider.CollidesWith<ColliderBox2D>(colliderBox))
                {
                    return new ColliderCastResult2D
                    {
                        hit = true,
                        collider = node.collider
                    };
                }
                else
                {
                    return ColliderCastResult2D.None;
                }

            }

            if (node.left >= 0)
            {
                ColliderCastResult2D leftResult = CastColliderBox(ref colliderBox, GetNode(node.left));
                if (leftResult.hit)
                {
                    return leftResult;
                }
            }

            if (node.right >= 0)
            {
                ColliderCastResult2D rightResult = CastColliderBox(ref colliderBox, GetNode(node.right));
                if (rightResult.hit)
                {
                    return rightResult;
                }
            }

            return ColliderCastResult2D.None;
        }

        public ColliderCastResult2D CastColliderSphere(ref ColliderSphere2D colliderSphere)
        {
            if (_nodeSize == 0)
            {
                return ColliderCastResult2D.None;
            }

            return CastColliderSphere(ref colliderSphere, _root);
        }

        private ColliderCastResult2D CastColliderSphere(ref ColliderSphere2D colliderSphere, Node node)
        {
            if (!colliderSphere.GetBoundingBox().Intersects(node.boundingBox)) return ColliderCastResult2D.None;

            if (node.IsLeaf)
            {
                if (node.collider.CollidesWith<ColliderSphere2D>(colliderSphere))
                {
                    return new ColliderCastResult2D
                    {
                        hit = true,
                        collider = node.collider
                    };
                }
                else
                {
                    return ColliderCastResult2D.None;
                }

            }

            if (node.left >= 0)
            {
                ColliderCastResult2D leftResult = CastColliderSphere(ref colliderSphere, GetNode(node.left));
                if (leftResult.hit)
                {
                    return leftResult;
                }
            }

            if (node.right >= 0)
            {
                ColliderCastResult2D rightResult = CastColliderSphere(ref colliderSphere, GetNode(node.right));
                if (rightResult.hit)
                {
                    return rightResult;
                }
            }

            return ColliderCastResult2D.None;
        }


        private void BuildBottomTop(NativeArrayList<ColliderRef2D> colliders)
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

            Node* ptr = _nodes.DataPtr;

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
                boundingBox = BoundingBox2D.Merge(GetNode(left).boundingBox, GetNode(right).boundingBox)
            };
        }

        private Node CreateLeaf(ColliderRef2D collider)
        {
            return new Node
            {
                left = -1,
                right = -1,
                collider = collider,
                boundingBox = collider.GetBoundingBox(),
            };
        }

        private void StartJobBuildLeaf(NativeArrayList<ColliderRef2D> colliders)
        {
            Node* ptr = _nodes.DataPtr;

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
            _nodes.DataPtr[_nodeSize] = node;
            _nodeSize++;
        }

        private struct JobCastRay : IJobBatch
        {
            public NativeBvh2D bvh;
            public Ray2D* rays;
            public RayCastResult2D* results;
            public void Execute(int index)
            {
                results[index] = bvh.CastRay(rays[index]);
            }
        }

        private struct JobCastRayFast : IJobBatch
        {
            public NativeBvh2D bvh;
            public Ray2D* rays;
            public RayCastResult2D* results;

            public void Execute(int index)
            {
                results[index] = bvh.CastRayFast(rays[index]);
            }
        }

        private struct JobCastColliderBox : IJobBatch
        {
            public NativeBvh2D bvh;
            public ColliderBox2D* colliderBoxes;
            public ColliderCastResult2D* results;

            public void Execute(int index)
            {
                results[index] = bvh.CastColliderBox(ref colliderBoxes[index]);
            }
        }

        private struct JobCastColliderSphere : IJobBatch
        {
            public NativeBvh2D bvh;
            public ColliderSphere2D* colliderSpheres;
            public ColliderCastResult2D* results;

            public void Execute(int index)
            {
                results[index] = bvh.CastColliderSphere(ref colliderSpheres[index]);
            }
        }
    }
}