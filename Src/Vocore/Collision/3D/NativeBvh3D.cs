using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;


namespace Vocore
{
    public unsafe struct NativeBvh3D : IDisposable
    {


        public struct Node
        {
            public int left;
            public int right;
            public BoundingBox3D boundingBox;
            public ColliderRef3D collider;
            public bool IsLeaf => collider.HasCollider;
        }

        private NativeBuffer<Node> _nodes;
        private NativeBuffer<RayCastResult3D> _batchRayCastResult;
        private NativeBuffer<ColliderCastResult3D> _batchColliderBoxCastResult;
        private NativeBuffer<ColliderCastResult3D> _batchColliderSphereCastResult;
        private Node _root;
        private int _nodeSize;
        private bool _isDisposed;

        public int Size => _nodeSize;
        public int Capacity => _nodes.Length;

        public RayCastResult3D CastRay(Ray3D ray)
        {
            if (_nodeSize == 0)
            {
                return RayCastResult3D.none;
            }

            return CastRayOptimized(ref ray, _root);
        }

        public RayCastResult3D CastRayFast(Ray3D ray)
        {
            if (_nodeSize == 0)
            {
                return RayCastResult3D.none;
            }

            return CastRayFast(ref ray, _root);
        }

        public NativeBuffer<RayCastResult3D> CastBatchRayFast(NativeArrayList<Ray3D> rays)
        {
            _batchRayCastResult.EnsureSizeNoCopy(rays.Length);
            JobCastRayFast job = new JobCastRayFast
            {
                bvh = this,
                rays = rays.UnsafePointer,
                results = _batchRayCastResult.UnsafePointer
            };
            job.RunParallel(rays.Length);

            return _batchRayCastResult;
        }

        public NativeBuffer<RayCastResult3D> CastBatchRay(NativeArrayList<Ray3D> rays)
        {
            _batchRayCastResult.EnsureSizeNoCopy(rays.Length);
            JobCastRay job = new JobCastRay
            {
                bvh = this,
                rays = rays.UnsafePointer,
                results = _batchRayCastResult.UnsafePointer
            };
            job.RunParallel(rays.Length);
            return _batchRayCastResult;
        }

        public NativeBuffer<ColliderCastResult3D> CastBatchColliderBox(NativeArrayList<ColliderBox3D> colliders)
        {
            _batchColliderBoxCastResult.EnsureSizeNoCopy(colliders.Length);
            JobCastColliderBox job = new JobCastColliderBox
            {
                bvh = this,
                colliderBoxes = colliders.UnsafePointer,
                results = _batchColliderBoxCastResult.UnsafePointer
            };
            job.RunParallel(colliders.Length);
            return _batchColliderBoxCastResult;
        }

        public NativeBuffer<ColliderCastResult3D> CastBatchColliderSphere(NativeArrayList<ColliderSphere3D> colliders)
        {
            _batchColliderSphereCastResult.EnsureSizeNoCopy(colliders.Length);
            JobCastColliderSphere job = new JobCastColliderSphere
            {
                bvh = this,
                colliderSpheres = colliders.UnsafePointer,
                results = _batchColliderSphereCastResult.UnsafePointer
            };
            job.RunParallel(colliders.Length);
            return _batchColliderSphereCastResult;
        }

        public void BuildTree(NativeArrayList<ColliderRef3D> colliders)
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
            return _nodes.UnsafePointer[index];
        }

        private RayCastResult3D CastRayFast(ref Ray3D ray, Node node)
        {
            //NativeStack<Node> stack = new NativeStack<Node>(_nodeSize * 2);
            Node* stack = stackalloc Node[_nodeSize / 2 + 2];
            int stackCount = 0;
            stack[stackCount++] = node;
            RayCastResult3D result = RayCastResult3D.none;


            while (stackCount > 0)
            {
                //Node top = stack.Pop();
                Node top = stack[--stackCount];

                if (!UtilsCollision3D.RayAABB(ray, top.boundingBox)) continue;

                if (top.IsLeaf)
                {
                    if (top.collider.IntersectRay(ray, out RaycastHit3D hitInfo))
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

        private RayCastResult3D CastRayOptimized(ref Ray3D ray, Node node)
        {
            //NativeStack<Node> stack = new NativeStack<Node>(_nodeSize * 2);
            Node* stack = stackalloc Node[_nodeSize / 2 + 2];
            int stackCount = 0;
            stack[stackCount++] = node;
            RayCastResult3D result = RayCastResult3D.none;


            while (stackCount > 0)
            {
                //Node top = stack.Pop();
                Node top = stack[--stackCount];

                if (!UtilsCollision3D.RayAABB(ray, top.boundingBox)) continue;

                if (top.IsLeaf)
                {
                    if (top.collider.IntersectRay(ray, out RaycastHit3D hitInfo))
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

        public ColliderCastResult3D CastColliderBox(ref ColliderBox3D colliderBox)
        {
            if (_nodeSize == 0)
            {
                return ColliderCastResult3D.None;
            }

            return CastColliderBox(ref colliderBox, _root);
        
        }

        private ColliderCastResult3D CastColliderBox(ref ColliderBox3D colliderBox, Node node)
        {
            if (!colliderBox.GetBoundingBox().Intersects(node.boundingBox)) return ColliderCastResult3D.None;

            if (node.IsLeaf)
            {
                if (node.collider.CollidesWith<ColliderBox3D>(colliderBox))
                {
                    return new ColliderCastResult3D
                    {
                        hit = true,
                        collider = node.collider
                    };
                }
                else
                {
                    return ColliderCastResult3D.None;
                }

            }

            if (node.left >= 0)
            {
                ColliderCastResult3D leftResult = CastColliderBox(ref colliderBox, GetNode(node.left));
                if (leftResult.hit)
                {
                    return leftResult;
                }
            }

            if (node.right >= 0)
            {
                ColliderCastResult3D rightResult = CastColliderBox(ref colliderBox, GetNode(node.right));
                if (rightResult.hit)
                {
                    return rightResult;
                }
            }

            return ColliderCastResult3D.None;
        }

        public ColliderCastResult3D CastColliderSphere(ref ColliderSphere3D colliderSphere)
        {
            if (_nodeSize == 0)
            {
                return ColliderCastResult3D.None;
            }

            return CastColliderSphere(ref colliderSphere, _root);
        }

        private ColliderCastResult3D CastColliderSphere(ref ColliderSphere3D colliderSphere, Node node)
        {
            if (!colliderSphere.GetBoundingBox().Intersects(node.boundingBox)) return ColliderCastResult3D.None;

            if (node.IsLeaf)
            {
                if (node.collider.CollidesWith<ColliderSphere3D>(colliderSphere))
                {
                    return new ColliderCastResult3D
                    {
                        hit = true,
                        collider = node.collider
                    };
                }
                else
                {
                    return ColliderCastResult3D.None;
                }

            }

            if (node.left >= 0)
            {
                ColliderCastResult3D leftResult = CastColliderSphere(ref colliderSphere, GetNode(node.left));
                if (leftResult.hit)
                {
                    return leftResult;
                }
            }

            if (node.right >= 0)
            {
                ColliderCastResult3D rightResult = CastColliderSphere(ref colliderSphere, GetNode(node.right));
                if (rightResult.hit)
                {
                    return rightResult;
                }
            }

            return ColliderCastResult3D.None;
        }


        private void BuildBottomTop(NativeArrayList<ColliderRef3D> colliders)
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

            Node* ptr = _nodes.UnsafePointer;

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
                boundingBox = BoundingBox3D.Merge(GetNode(left).boundingBox, GetNode(right).boundingBox)
            };
        }

        private Node CreateLeaf(ColliderRef3D collider)
        {
            return new Node
            {
                left = -1,
                right = -1,
                collider = collider,
                boundingBox = collider.GetBoundingBox(),
            };
        }

        private void StartJobBuildLeaf(NativeArrayList<ColliderRef3D> colliders)
        {
            Node* ptr = _nodes.UnsafePointer;

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
            _nodes.UnsafePointer[_nodeSize] = node;
            _nodeSize++;
        }

        private struct JobCastRay : IJobBatch
        {
            public NativeBvh3D bvh;
            public Ray3D* rays;
            public RayCastResult3D* results;
            public void Execute(int index)
            {
                results[index] = bvh.CastRay(rays[index]);
            }
        }

        private struct JobCastRayFast : IJobBatch
        {
            public NativeBvh3D bvh;
            public Ray3D* rays;
            public RayCastResult3D* results;

            public void Execute(int index)
            {
                results[index] = bvh.CastRayFast(rays[index]);
            }
        }

        private struct JobCastColliderBox : IJobBatch
        {
            public NativeBvh3D bvh;
            public ColliderBox3D* colliderBoxes;
            public ColliderCastResult3D* results;

            public void Execute(int index)
            {
                results[index] = bvh.CastColliderBox(ref colliderBoxes[index]);
            }
        }

        private struct JobCastColliderSphere : IJobBatch
        {
            public NativeBvh3D bvh;
            public ColliderSphere3D* colliderSpheres;
            public ColliderCastResult3D* results;

            public void Execute(int index)
            {
                results[index] = bvh.CastColliderSphere(ref colliderSpheres[index]);
            }
        }
    }
}