using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;


namespace Vocore
{
    public unsafe class NativeBvh2D : IDisposable
    {
        private const int ChildCount = 2;

        public struct Node
        {
            public int left;
            public int right;
            public BoundingBox2D boundingBox;
            public ColliderRef2D collider;
            public bool IsLeaf => collider.HasCollider;
        }

        private class JobCastRay : IJobBatch
        {
            private NativeBvh2D _bvh;
            public Ray2D* rays;
            public RayCastResult2D* results;

            public JobCastRay(NativeBvh2D bvh)
            {
                _bvh = bvh;
            }

            public void Execute(int index)
            {
                results[index] = _bvh.CastRay(rays[index]);
            }
        }

        private class JobCastRayFirstHit : IJobBatch
        {
            private NativeBvh2D _bvh;
            public Ray2D* rays;
            public RayCastResult2D* results;

            public JobCastRayFirstHit(NativeBvh2D bvh)
            {
                _bvh = bvh;
            }

            public void Execute(int index)
            {
                results[index] = _bvh.CastRayFirstHit(rays[index]);
            }
        }

        private class JobCastColliderRef : IJobBatch
        {
            private NativeBvh2D _bvh;
            public ColliderRef2D* colliders;
            public ColliderCastResult2D* results;

            public JobCastColliderRef(NativeBvh2D bvh)
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
            private NativeBvh2D _bvh;
            public ColliderRef2D* colliders;
            public NativeArrayList<ColliderCastResult2D>* results;

            public JobCastColliderRefCollector(NativeBvh2D bvh)
            {
                _bvh = bvh;
            }

            public void Execute(int index)
            {
                _bvh.CastColliderCollectorCore(colliders[index], _bvh._root, results + index);
            }
        }

        private readonly ParallelScheduler _scheduler;
        //reuse job
        private readonly JobCastRay _jobCastRay;
        private readonly JobCastRayFirstHit _jobCastRayFirstHit;
        private readonly JobCastColliderRef _jobCastColliderRef;
        private readonly JobCastColliderRefCollector _jobCastColliderRefCollector;


        private NativeBuffer<Node> _nodes;
        // result for parallel job
        private NativeBuffer<RayCastResult2D> _batchRayCastResult;
        private NativeBuffer<ColliderCastResult2D> _batchColliderCastResult;
        private NativeBuffer<NativeArrayList<ColliderCastResult2D>> _batchColliderCastResultCollector;

        //result for single job
        private NativeArrayList<ColliderCastResult2D> _castResultCollector;

        private Node _root;
        private int _nodeSize;
        private int _treeDepth;
        private bool _isDisposed;

        public int Size => _nodeSize;
        public int Capacity => _nodes.Length;

        public NativeBvh2D(ParallelScheduler scheduler)
        {
            _scheduler = scheduler;

            _jobCastRay = new JobCastRay(this);
            _jobCastRayFirstHit = new JobCastRayFirstHit(this);
            _jobCastColliderRef = new JobCastColliderRef(this);
            _jobCastColliderRefCollector = new JobCastColliderRefCollector(this);
            _isDisposed = false;

            _castResultCollector = new NativeArrayList<ColliderCastResult2D>(4);
        }

        public RayCastResult2D CastRay(Ray2D ray)
        {
            if (_nodeSize == 0)
            {
                return RayCastResult2D.none;
            }

            return CastRay(ref ray, _root);
        }

        public RayCastResult2D CastRayFirstHit(Ray2D ray)
        {
            if (_nodeSize == 0)
            {
                return RayCastResult2D.none;
            }

            return CastRayFirstHit(ref ray, _root);
        }

        public MemoryRef<RayCastResult2D> CastBatchRayFirstHit(MemoryRef<Ray2D> rays)
        {
            _batchRayCastResult.EnsureSizeWithoutCopy(rays.Length);
            if (_nodeSize == 0)
            {
                for (int i = 0; i < rays.Length; i++)
                {
                    _batchRayCastResult[i] = RayCastResult2D.none;
                }
                return _batchRayCastResult.MemoryRef;
            }

            _jobCastRayFirstHit.rays = rays.Pointer;
            _jobCastRayFirstHit.results = _batchRayCastResult.UnsafePointer;
            _scheduler.Run(_jobCastRayFirstHit, rays.Length);

            return _batchRayCastResult.MemoryRef;
        }

        public MemoryRef<RayCastResult2D> CastBatchRay(MemoryRef<Ray2D> rays)
        {
            _batchRayCastResult.EnsureSizeWithoutCopy(rays.Length);
            if (_nodeSize == 0)
            {
                for (int i = 0; i < rays.Length; i++)
                {
                    _batchRayCastResult[i] = RayCastResult2D.none;
                }
                return _batchRayCastResult.MemoryRef;
            }

            _jobCastRay.rays = rays.Pointer;
            _jobCastRay.results = _batchRayCastResult.UnsafePointer;
            _scheduler.Run(_jobCastRay, rays.Length);
            return _batchRayCastResult.MemoryRef;
        }

        public MemoryRef<ColliderCastResult2D> CastBatchColliderRef(MemoryRef<ColliderRef2D> colliders)
        {
            _batchColliderCastResult.EnsureSizeWithoutCopy(colliders.Length);

            if (_nodeSize == 0)
            {
                for (int i = 0; i < colliders.Length; i++)
                {
                    _batchColliderCastResult[i] = ColliderCastResult2D.None;
                }
                return _batchColliderCastResult.MemoryRef;
            }

            _jobCastColliderRef.colliders = colliders.Pointer;
            _jobCastColliderRef.results = _batchColliderCastResult.UnsafePointer;
            _scheduler.Run(_jobCastColliderRef, colliders.Length);

            return _batchColliderCastResult.MemoryRef;
        }

        public MemoryRef<NativeArrayList<ColliderCastResult2D>> CastBatchColliderRefCollector(MemoryRef<ColliderRef2D> colliders)
        {
            int lengthBefore = _batchColliderCastResultCollector.Length;
            _batchColliderCastResultCollector.EnsureSize(colliders.Length);
            int allocCount = _batchColliderCastResultCollector.Length - lengthBefore;

            if (_nodeSize == 0)
            {
                for (int i = 0; i < colliders.Length; i++)
                {
                    _batchColliderCastResultCollector.UnsafePointer[i].Clear();
                }
                return _batchColliderCastResultCollector.MemoryRef;
            }

            for (int i = 0; i < allocCount; i++)
            {
                _batchColliderCastResultCollector[lengthBefore + i] = new NativeArrayList<ColliderCastResult2D>(4);
            }

            for (int i = 0; i < colliders.Length; i++)
            {
                NativeArrayList<ColliderCastResult2D>* collector = _batchColliderCastResultCollector.UnsafePointer + i;
                collector->Clear();
            }

            _jobCastColliderRefCollector.colliders = colliders.Pointer;
            _jobCastColliderRefCollector.results = _batchColliderCastResultCollector.UnsafePointer;
            _scheduler.Run(_jobCastColliderRefCollector, colliders.Length);

            return _batchColliderCastResultCollector.MemoryRef;
        }

        public void BuildTree(MemoryRef<ColliderRef2D> colliders)
        {
            _nodes.EnsureSizeWithoutCopy(colliders.Length * 2 + (int)math.sqrt(colliders.Length) + 2);
            BuildBottomTop(colliders);
            _treeDepth = (int)math.log2(colliders.Length + 1) + 1;
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

        //cast collision for single result

        private RayCastResult2D CastRayFirstHit(ref Ray2D ray, Node node)
        {
            //NativeStack<Node> stack = new NativeStack<Node>(_nodeSize * 2);
            Node* stack = stackalloc Node[_treeDepth];
            int stackCount = 0;
            stack[stackCount++] = node;
            RayCastResult2D result = RayCastResult2D.none;
            BoundingBox2D rayBox = ray.GetBoundingBox();

            while (stackCount > 0)
            {
                //Node top = stack.Pop();
                Node top = stack[--stackCount];

                //if (!UtilsCollision2D.RayAABB(ray, top.boundingBox)) continue;
                if (!rayBox.Intersects(top.boundingBox)) continue;

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

        private RayCastResult2D CastRay(ref Ray2D ray, Node node)
        {
            //NativeStack<Node> stack = new NativeStack<Node>(_nodeSize * 2);
            Node* stack = stackalloc Node[_treeDepth];
            int stackCount = 0;
            stack[stackCount++] = node;
            RayCastResult2D result = RayCastResult2D.none;

            BoundingBox2D rayBox = ray.GetBoundingBox();

            while (stackCount > 0)
            {
                //Node top = stack.Pop();
                Node top = stack[--stackCount];

                //if (!UtilsCollision2D.RayAABB(ray, top.boundingBox)) continue;
                if (!rayBox.Intersects(top.boundingBox)) continue;

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

        public ColliderCastResult2D CastCollider<T>(T collider) where T : unmanaged, ICollider2D
        {
            if (_nodeSize == 0)
            {
                return ColliderCastResult2D.None;
            }

            ColliderRef2D reference = ColliderRef2D.Create(&collider);

            return CastColliderCore(reference, _root);
        }

        public ColliderCastResult2D CastCollider(ColliderRef2D collider)
        {
            if (_nodeSize == 0)
            {
                return ColliderCastResult2D.None;
            }

            return CastColliderCore(collider, _root);
        }

        //cast collision for multiple result

        public MemoryRef<ColliderCastResult2D> CastPointRefCollector(Vector2 point)
        {
            _castResultCollector.Clear();
            NativeArrayList<ColliderCastResult2D> tmpResult = _castResultCollector;
            if (_nodeSize > 0)
            {
                CastPointCollectorCore(point, _root, &tmpResult);
                _castResultCollector = tmpResult;
            }
            return _castResultCollector.MemoryRef;
        }

        public MemoryRef<ColliderCastResult2D> CastColliderRefCollector(ColliderRef2D collider)
        {
            _castResultCollector.Clear();
            NativeArrayList<ColliderCastResult2D> tmpResult = _castResultCollector;
            if (_nodeSize > 0)
            {
                CastColliderCollectorCore(collider, _root, &tmpResult);
                _castResultCollector = tmpResult;
            }

            return _castResultCollector.MemoryRef;
        }

        // cast collider implementation

        private ColliderCastResult2D CastColliderCore(ColliderRef2D collider, Node node)
        {
            Node* stack = stackalloc Node[_treeDepth];
            int stackCount = 0;
            stack[stackCount++] = node;
            ColliderCastResult2D result = ColliderCastResult2D.None;
            BoundingBox2D aabb = collider.GetBoundingBox();


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

        private void CastColliderCollectorCore(ColliderRef2D collider, Node node, NativeArrayList<ColliderCastResult2D>* result)
        {
            Node* stack = stackalloc Node[_treeDepth];
            int stackCount = 0;
            stack[stackCount++] = node;
            BoundingBox2D aabb = collider.GetBoundingBox();


            while (stackCount > 0)
            {
                //Node top = stack.Pop();
                Node top = stack[--stackCount];

                if (!aabb.Intersects(top.boundingBox)) continue;

                if (top.IsLeaf)
                {
                    if (top.collider.CollidesWith(collider))
                    {
                        ColliderCastResult2D resultItem = new ColliderCastResult2D
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

        private void CastPointCollectorCore(Vector2 point, Node node, NativeArrayList<ColliderCastResult2D>* result)
        {
            Node* stack = stackalloc Node[_treeDepth];
            int stackCount = 0;
            stack[stackCount++] = node;

            while (stackCount > 0)
            {
                //Node top = stack.Pop();
                Node top = stack[--stackCount];

                if (!top.boundingBox.Contains(point)) continue;

                if (top.IsLeaf)
                {
                    if (top.collider.IntersectPoint(point))
                    {
                        ColliderCastResult2D resultItem = new ColliderCastResult2D
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


        private void BuildBottomTop(MemoryRef<ColliderRef2D> colliders)
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

        private void StartJobBuildLeaf(MemoryRef<ColliderRef2D> colliders)
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

            _batchColliderCastResultCollector.Dispose();
            _batchColliderCastResult.Dispose();
            _castResultCollector.Dispose();

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