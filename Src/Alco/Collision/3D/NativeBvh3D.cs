using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;


namespace Alco
{
    public unsafe class NativeBvh3D : IDisposable
    {
        private const int BatchSize = 16;

        private const int ChildCount = 2;

        public struct Node
        {
            public int left;
            public int right;
            public BoundingBox3D boundingBox;
            public ColliderRef3D collider;
            public bool IsLeaf => collider.HasCollider;
        }

        private class CastRayTask : ReuseableBatchTask
        {
            private NativeBvh3D _bvh;
            public Ray3D* rays;
            public RayCastResult3D* results;

            public CastRayTask(NativeBvh3D bvh)
            {
                _bvh = bvh;
            }

            protected override void ExecuteCore(int index)
            {
                results[index] = _bvh.CastRay(rays[index]);
            }
        }

        private class CastRayFastTask : ReuseableBatchTask
        {
            private NativeBvh3D _bvh;
            public Ray3D* rays;
            public RayCastResult3D* results;

            public CastRayFastTask(NativeBvh3D bvh)
            {
                _bvh = bvh;
            }

            protected override void ExecuteCore(int index)
            {
                results[index] = _bvh.CastRayFirstHit(rays[index]);
            }
        }

        private class CastColliderRefTask : ReuseableBatchTask
        {
            private NativeBvh3D _bvh;
            public ColliderRef3D* colliders;
            public ColliderCastResult3D* results;

            public CastColliderRefTask(NativeBvh3D bvh)
            {
                _bvh = bvh;
            }

            protected override void ExecuteCore(int index)
            {
                results[index] = _bvh.CastCollider(colliders[index]);
            }
        }

        private class CastColliderRefCollectorTask : ReuseableBatchTask
        {
            private NativeBvh3D _bvh;
            public ColliderRef3D* colliders;
            public NativeArrayList<ColliderCastResult3D>* results;

            public CastColliderRefCollectorTask(NativeBvh3D bvh)
            {
                _bvh = bvh;
            }

            protected override void ExecuteCore(int index)
            {
                _bvh.CastColliderCollectorCore(colliders[index], _bvh._root, results + index);
            }
        }

        //reuse task
        private readonly CastRayTask _castRayTask;
        private readonly CastRayFastTask _castRayFastTask;
        private readonly CastColliderRefTask _castColliderRefTask;
        private readonly CastColliderRefCollectorTask _castColliderRefCollectorTask;

        private NativeBuffer<Node> _nodes;
        // result for parallel job
        private NativeBuffer<RayCastResult3D> _batchRayCastResult;
        private NativeBuffer<ColliderCastResult3D> _batchColliderCastResult;
        private NativeBuffer<NativeArrayList<ColliderCastResult3D>> _batchColliderCastResultCollector;

        //result for single job
        private NativeArrayList<ColliderCastResult3D> _castResultCollector;

        private Node _root;
        private int _nodeSize;
        private int _treeDepth;
        private bool _isDisposed;

        public int Size => _nodeSize;
        public int Capacity => _nodes.Length;

        public NativeBvh3D()
        {
            _castRayTask = new CastRayTask(this);
            _castRayFastTask = new CastRayFastTask(this);
            _castColliderRefTask = new CastColliderRefTask(this);
            _castColliderRefCollectorTask = new CastColliderRefCollectorTask(this);
            _isDisposed = false;

            _castResultCollector = new NativeArrayList<ColliderCastResult3D>(4);
        }

        public RayCastResult3D CastRay(Ray3D ray)
        {
            if (_nodeSize == 0)
            {
                return RayCastResult3D.none;
            }

            return CastRay(ref ray, _root);
        }

        public RayCastResult3D CastRayFirstHit(Ray3D ray)
        {
            if (_nodeSize == 0)
            {
                return RayCastResult3D.none;
            }

            return CastRayFirstHit(ref ray, _root);
        }

        public ReadOnlySpan<RayCastResult3D> CastBatchRayFirstHit(ReadOnlySpan<Ray3D> rays)
        {
            _batchRayCastResult.EnsureSizeWithoutCopy(rays.Length);
            if (_nodeSize == 0)
            {
                for (int i = 0; i < rays.Length; i++)
                {
                    _batchRayCastResult[i] = RayCastResult3D.none;
                }
                return _batchRayCastResult.AsReadOnlySpan();
            }

            fixed (Ray3D* raysPtr = rays)
            {
                _castRayFastTask.rays = raysPtr;
                _castRayFastTask.results = _batchRayCastResult.UnsafePointer;
                _castRayFastTask.RunParallel(rays.Length, BatchSize);
            }


            return _batchRayCastResult.AsReadOnlySpan();
        }

        public ReadOnlySpan<RayCastResult3D> CastBatchRay(ReadOnlySpan<Ray3D> rays)
        {
            _batchRayCastResult.EnsureSizeWithoutCopy(rays.Length);
            if (_nodeSize == 0)
            {
                for (int i = 0; i < rays.Length; i++)
                {
                    _batchRayCastResult[i] = RayCastResult3D.none;
                }
                return _batchRayCastResult.AsReadOnlySpan();
            }

            fixed (Ray3D* raysPtr = rays)
            {
                _castRayTask.rays = raysPtr;
                _castRayTask.results = _batchRayCastResult.UnsafePointer;
                _castRayTask.RunParallel(rays.Length, BatchSize);
            }
            return _batchRayCastResult.AsReadOnlySpan();
        }

        public ReadOnlySpan<ColliderCastResult3D> CastBatchColliderRef(ReadOnlySpan<ColliderRef3D> colliders)
        {
            _batchColliderCastResult.EnsureSizeWithoutCopy(colliders.Length);

            if (_nodeSize == 0)
            {
                for (int i = 0; i < colliders.Length; i++)
                {
                    _batchColliderCastResult[i] = ColliderCastResult3D.None;
                }
                return _batchColliderCastResult.AsReadOnlySpan();
            }

            fixed (ColliderRef3D* collidersPtr = colliders)
            {
                _castColliderRefTask.colliders = collidersPtr;
                _castColliderRefTask.results = _batchColliderCastResult.UnsafePointer;
                _castColliderRefTask.RunParallel(colliders.Length, BatchSize);
            }

            return _batchColliderCastResult.AsReadOnlySpan();
        }

        public ReadOnlySpan<NativeArrayList<ColliderCastResult3D>> CastBatchColliderRefCollector(ReadOnlySpan<ColliderRef3D> colliders)
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
                return _batchColliderCastResultCollector.AsReadOnlySpan();
            }

            for (int i = 0; i < allocCount; i++)
            {
                _batchColliderCastResultCollector[lengthBefore + i] = new NativeArrayList<ColliderCastResult3D>(4);
            }

            for (int i = 0; i < colliders.Length; i++)
            {
                NativeArrayList<ColliderCastResult3D>* collector = _batchColliderCastResultCollector.UnsafePointer + i;
                collector->Clear();
            }

            fixed (ColliderRef3D* collidersPtr = colliders)
            {
                _castColliderRefCollectorTask.colliders = collidersPtr;
                _castColliderRefCollectorTask.results = _batchColliderCastResultCollector.UnsafePointer;
                _castColliderRefCollectorTask.RunParallel(colliders.Length, BatchSize);
            }

            return _batchColliderCastResultCollector.AsReadOnlySpan();
        }

        public void BuildTree(ReadOnlySpan<ColliderRef3D> colliders)
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

        private RayCastResult3D CastRayFirstHit(ref Ray3D ray, Node node)
        {
            //NativeStack<Node> stack = new NativeStack<Node>(_nodeSize * 2);
            Node* stack = stackalloc Node[_treeDepth];
            int stackCount = 0;
            stack[stackCount++] = node;
            RayCastResult3D result = RayCastResult3D.none;
            BoundingBox3D rayBox = ray.GetBoundingBox();


            while (stackCount > 0)
            {
                //Node top = stack.Pop();
                Node top = stack[--stackCount];

                //if (!UtilsCollision3D.RayAABB(ray, top.boundingBox)) continue;
                if (!rayBox.Intersects(top.boundingBox)) continue;

                if (top.IsLeaf)
                {
                    if (top.collider.IntersectRay(ray, out RaycastHit3D hitInfo))
                    {
                        result.Hit = true;
                        result.HitInfo = hitInfo;
                        result.Collider = top.collider;
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
            Node* stack = stackalloc Node[_treeDepth];
            int stackCount = 0;
            stack[stackCount++] = node;
            RayCastResult3D result = RayCastResult3D.none;

            BoundingBox3D rayBox = ray.GetBoundingBox();


            while (stackCount > 0)
            {
                //Node top = stack.Pop();
                Node top = stack[--stackCount];

                //if (!UtilsCollision3D.RayAABB(ray, top.boundingBox)) continue;
                if (!rayBox.Intersects(top.boundingBox)) continue;

                if (top.IsLeaf)
                {
                    if (top.collider.IntersectRay(ray, out RaycastHit3D hitInfo))
                    {  
                        if (!result.Hit ||result.Hit && hitInfo.Fraction < result.HitInfo.Fraction)
                        {
                            result.Hit = true;
                            result.HitInfo = hitInfo;
                            result.Collider = top.collider;
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

        //cast collision for multiple result

        public ReadOnlySpan<ColliderCastResult3D> CastPointRefCollector(Vector3 point)
        {
            _castResultCollector.Clear();
            NativeArrayList<ColliderCastResult3D> tmpResult = _castResultCollector;
            if (_nodeSize > 0)
            {
                CastPointCollectorCore(point, _root, &tmpResult);
                _castResultCollector = tmpResult;
            }
            return _castResultCollector.AsReadOnlySpan();
        }

        public ReadOnlySpan<ColliderCastResult3D> CastColliderRefCollector(ColliderRef3D collider)
        {
            _castResultCollector.Clear();
            NativeArrayList<ColliderCastResult3D> tmpResult = _castResultCollector;
            if (_nodeSize > 0)
            {
                CastColliderCollectorCore(collider, _root, &tmpResult);
                _castResultCollector = tmpResult;
            }

            return _castResultCollector.AsReadOnlySpan();
        }

        // cast collider implementation

        private ColliderCastResult3D CastColliderCore(ColliderRef3D collider, Node node) 
        {
            Node* stack = stackalloc Node[_treeDepth];
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

                        result.Hit = true;
                        result.Collider = top.collider;

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
            Node* stack = stackalloc Node[_treeDepth];
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
                            Hit = true,
                            Collider = top.collider
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

        private void CastPointCollectorCore(Vector3 point, Node node, NativeArrayList<ColliderCastResult3D>* result)
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
                        ColliderCastResult3D resultItem = new ColliderCastResult3D
                        {
                            Hit = true,
                            Collider = top.collider
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


        private void BuildBottomTop(ReadOnlySpan<ColliderRef3D> colliders)
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

        private void StartJobBuildLeaf(ReadOnlySpan<ColliderRef3D> colliders)
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

            _castRayTask?.Dispose();
            _castRayFastTask?.Dispose();
            _castColliderRefTask?.Dispose();
            _castColliderRefCollectorTask?.Dispose();

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