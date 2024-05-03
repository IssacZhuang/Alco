using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;


namespace Vocore
{
    public unsafe class NativeBvh3D : IDisposable
    {
        private const int ChildCount = 2;

        public struct Node
        {
            public int left;
            public int right;
            public BoundingBox3D boundingBox;
            public ColliderRef3D collider;
            public bool IsLeaf => collider.HasCollider;
        }

        private class JobCastRay : IJobBatch
        {
            private NativeBvh3D _bvh;
            public Ray3D* rays;
            public RayCastResult3D* results;

            public JobCastRay(NativeBvh3D bvh)
            {
                _bvh = bvh;
            }

            public void Execute(int index)
            {
                results[index] = _bvh.CastRay(rays[index]);
            }
        }

        private class JobCastRayFast : IJobBatch
        {
            private NativeBvh3D _bvh;
            public Ray3D* rays;
            public RayCastResult3D* results;

            public JobCastRayFast(NativeBvh3D bvh)
            {
                _bvh = bvh;
            }

            public void Execute(int index)
            {
                results[index] = _bvh.CastRayFast(rays[index]);
            }
        }

        private class JobCastColliderRef : IJobBatch
        {
            private NativeBvh3D _bvh;
            public ColliderRef3D* colliders;
            public ColliderCastResult3D* results;

            public JobCastColliderRef(NativeBvh3D bvh)
            {
                _bvh = bvh;
            }


            public void Execute(int index)
            {
                results[index] = _bvh.CastCollider(colliders[index]);
            }
        }

        private class JobCastColliderRefCollector : IJobBatch
        {
            private NativeBvh3D _bvh;
            public ColliderRef3D* colliders;
            public NativeArrayList<ColliderCastResult3D>* results;

            public JobCastColliderRefCollector(NativeBvh3D bvh)
            {
                _bvh = bvh;
            }

            public void Execute(int index)
            {
                //TODO: this is tmp operation, it will cause memory leak
                results[index] = new NativeArrayList<ColliderCastResult3D>(4);
                _bvh.CastColliderCollectorCore(colliders[index], _bvh._root, results + index);
            }
        } 

        private readonly ParallelScheduler _scheduler;
        //reuse job
        private readonly JobCastRay _jobCastRay;
        private readonly JobCastRayFast _jobCastRayFast;
        private readonly JobCastColliderRef _jobCastColliderRef;
        private readonly JobCastColliderRefCollector _jobCastColliderRefCollector;


        private NativeBuffer<Node> _nodes;
        private NativeBuffer<RayCastResult3D> _batchRayCastResult;
        private NativeBuffer<ColliderCastResult3D> _batchColliderCastResult;
        private NativeBuffer<NativeArrayList<ColliderCastResult3D>> _batchColliderCastResultCollector;


        private Node _root;
        private int _nodeSize;
        private bool _isDisposed;

        public int Size => _nodeSize;
        public int Capacity => _nodes.Length;

        public NativeBvh3D(ParallelScheduler scheduler)
        {
            _scheduler = scheduler;

            _jobCastRay = new JobCastRay(this);
            _jobCastRayFast = new JobCastRayFast(this);
            _jobCastColliderRef = new JobCastColliderRef(this);
            _jobCastColliderRefCollector = new JobCastColliderRefCollector(this);
            _isDisposed = false;
        }

        public RayCastResult3D CastRay(Ray3D ray)
        {
            if (_nodeSize == 0)
            {
                return RayCastResult3D.none;
            }

            return CastRay(ref ray, _root);
        }

        public RayCastResult3D CastRayFast(Ray3D ray)
        {
            if (_nodeSize == 0)
            {
                return RayCastResult3D.none;
            }

            return CastRayFast(ref ray, _root);
        }

        public MemoryRef<RayCastResult3D> CastBatchRayFast(MemoryRef<Ray3D> rays)
        {
            _batchRayCastResult.EnsureSizeWithoutCopy(rays.Length);

            _jobCastRayFast.rays = rays.Pointer;
            _jobCastRayFast.results = _batchRayCastResult.UnsafePointer;
            _scheduler.Run(_jobCastRayFast, rays.Length);

            return _batchRayCastResult.MemoryRef;
        }

        public MemoryRef<RayCastResult3D> CastBatchRay(MemoryRef<Ray3D> rays)
        {
            _batchRayCastResult.EnsureSizeWithoutCopy(rays.Length);

            _jobCastRay.rays = rays.Pointer;
            _jobCastRay.results = _batchRayCastResult.UnsafePointer;
            _scheduler.Run(_jobCastRay, rays.Length);
            return _batchRayCastResult.MemoryRef;
        }

        public MemoryRef<ColliderCastResult3D> CastBatchColliderRef(MemoryRef<ColliderRef3D> colliders)
        {
            _batchColliderCastResult.EnsureSizeWithoutCopy(colliders.Length);

            _jobCastColliderRef.colliders = colliders.Pointer;
            _jobCastColliderRef.results = _batchColliderCastResult.UnsafePointer;
            _scheduler.Run(_jobCastColliderRef, colliders.Length);

            return _batchColliderCastResult.MemoryRef;
        }

        public MemoryRef<NativeArrayList<ColliderCastResult3D>> CastBatchColliderRefCollector(MemoryRef<ColliderRef3D> colliders)
        {
            // EnsureSizeWithoutCopy will cause memory leak here
            _batchColliderCastResultCollector.EnsureSize(colliders.Length);

            _jobCastColliderRefCollector.colliders = colliders.Pointer;
            _jobCastColliderRefCollector.results = _batchColliderCastResultCollector.UnsafePointer;
            _scheduler.Run(_jobCastColliderRefCollector, colliders.Length);

            return _batchColliderCastResultCollector.MemoryRef;
        }

        public void BuildTree(MemoryRef<ColliderRef3D> colliders)
        {
            _nodes.EnsureSizeWithoutCopy(colliders.Length * 2 + (int)math.sqrt(colliders.Length) + 2);
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

        private RayCastResult3D CastRay(ref Ray3D ray, Node node)
        {
            //NativeStack<Node> stack = new NativeStack<Node>(_nodeSize * 2);
            Node* stack = stackalloc Node[_nodeSize / ChildCount + ChildCount];
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

        public ColliderCastResult3D CastCollider<T>(T collider) where T : unmanaged, ICollider3D
        {
            if (_nodeSize == 0)
            {
                return ColliderCastResult3D.None;
            }

            ColliderRef3D reference = ColliderRef3D.Create(&collider);

            return CastColliderCore(reference, _root);
        }

        public ColliderCastResult3D CastCollider(ColliderRef3D collider)
        {
            if (_nodeSize == 0)
            {
                return ColliderCastResult3D.None;
            }

            return CastColliderCore(collider, _root);
        }



        private ColliderCastResult3D CastColliderCore(ColliderRef3D collider, Node node) 
        {
            Node* stack = stackalloc Node[_nodeSize / ChildCount + ChildCount];
            int stackCount = 0;
            stack[stackCount++] = node;
            ColliderCastResult3D result = ColliderCastResult3D.None;
            BoundingBox3D aabb = collider.GetBoundingBox();


            while (stackCount > 0)
            {
                //Node top = stack.Pop();
                Node top = stack[--stackCount];

                if (!aabb.Intersects(top.boundingBox)) continue;

                if (top.IsLeaf)
                {
                    if (top.collider.CollidesWith(collider))
                    {

                        result.hit = true;
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

        private void CastColliderCollectorCore(ColliderRef3D collider, Node node, NativeArrayList<ColliderCastResult3D>* result)
        {
            Node* stack = stackalloc Node[_nodeSize / ChildCount + ChildCount];
            int stackCount = 0;
            stack[stackCount++] = node;
            BoundingBox3D aabb = collider.GetBoundingBox();


            while (stackCount > 0)
            {
                //Node top = stack.Pop();
                Node top = stack[--stackCount];

                if (!aabb.Intersects(top.boundingBox)) continue;

                if (top.IsLeaf)
                {
                    if (top.collider.CollidesWith(collider))
                    {
                        ColliderCastResult3D resultItem = new ColliderCastResult3D
                        {
                            hit = true,
                            collider = top.collider
                        };
                        result->Add(resultItem);
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

        }


        private void BuildBottomTop(MemoryRef<ColliderRef3D> colliders)
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

        private void StartJobBuildLeaf(MemoryRef<ColliderRef3D> colliders)
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

            for (int i = 0; i < _batchColliderCastResultCollector.Length; i++)
            {
                _batchColliderCastResultCollector[i].Dispose();
            }

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


    }
}