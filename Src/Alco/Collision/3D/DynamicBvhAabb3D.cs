using System;
using System.Buffers;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Alco
{
    /// <summary>
    /// Incremental 3D dynamic AABB tree with surface-area-heuristic (SAH) insertion,
    /// AVL height-based tree rotations, optional AABB fattening, and Morton bottom-up <c>BulkBuild</c>.
    /// Read queries are thread-safe concurrently when no mutation is in progress.
    /// </summary>
    public unsafe sealed class DynamicBvhAabb3D
    {
        /// <summary>Sentinel node handle indicating null / unallocated node.</summary>
        public const int NullNode = -1;

        private const int StackAllocThreshold = 256;
        private const int MaxMortonBuildDepth = 64;

        private struct Node
        {
            public BoundingBox3D Bounds;
            public int ParentOrNextFree;
            public int Left;
            public int Right;
            public int Height;
            public int Payload;
            public bool IsAllocated;
        }

        private readonly struct BulkItem
        {
            public readonly BoundingBox3D Bounds;
            public readonly int OriginalIndex;
            public readonly int Payload;

            public BulkItem(BoundingBox3D bounds, int originalIndex, int payload)
            {
                Bounds = bounds;
                OriginalIndex = originalIndex;
                Payload = payload;
            }
        }

        private struct ListLeafCollector : IBvhAabb3DLeafVisitor
        {
            private readonly List<int> _results;

            public ListLeafCollector(List<int> results) => _results = results;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool OnLeaf(int payload)
            {
                _results.Add(payload);
                return true;
            }
        }

        private Node[] _nodes;
        private int _root = NullNode;
        private int _nodeCount;
        private int _leafCount;
        private int _freeList;

        /// <summary>Gets or sets the AABB inflation margin applied around stored leaf bounds to absorb minor movement.</summary>
        public float FattenMargin { get; set; }

        /// <summary>Gets the number of active leaf items currently stored in the tree.</summary>
        public int Count => _leafCount;

        /// <summary>Gets total node slot capacity currently allocated.</summary>
        public int Capacity => _nodes.Length;

        /// <summary>Gets the union bounds of all active items, or <c>default</c> when empty.</summary>
        public BoundingBox3D RootBounds => _root != NullNode ? _nodes[_root].Bounds : default;

        /// <summary>
        /// Initializes an empty <see cref="DynamicBvhAabb3D"/> with the specified capacity and optional AABB fatten margin.
        /// </summary>
        public DynamicBvhAabb3D(int initialCapacity = 64, float fattenMargin = 0f)
        {
            int cap = Math.Max(4, initialCapacity);
            FattenMargin = Math.Max(0f, fattenMargin);
            _nodes = new Node[cap];
            InitFreeList(0, cap);
        }

        /// <summary>Removes all items while retaining allocated capacity.</summary>
        public void Clear()
        {
            Array.Clear(_nodes);
            _root = NullNode;
            _nodeCount = 0;
            _leafCount = 0;
            InitFreeList(0, _nodes.Length);
        }

        /// <summary>
        /// Rebuilds the entire tree in balanced 30-bit Morton order over <paramref name="bounds"/> with sequential payloads <c>0..bounds.Length-1</c>,
        /// returning the created node handles in <paramref name="outHandles"/>.
        /// </summary>
        public void BulkBuild(ReadOnlySpan<BoundingBox3D> bounds, Span<int> outHandles)
        {
            BulkBuildCore(bounds, ReadOnlySpan<int>.Empty, hasExplicitPayloads: false, outHandles);
        }

        /// <summary>
        /// Rebuilds the entire tree in balanced 30-bit Morton order over <paramref name="bounds"/> and explicit <paramref name="payloads"/>,
        /// returning the created node handles in <paramref name="outHandles"/>.
        /// </summary>
        public void BulkBuild(ReadOnlySpan<BoundingBox3D> bounds, ReadOnlySpan<int> payloads, Span<int> outHandles)
        {
            if (payloads.Length < bounds.Length)
            {
                throw new ArgumentException("Payloads span length must be at least bounds span length.", nameof(payloads));
            }
            BulkBuildCore(bounds, payloads, hasExplicitPayloads: true, outHandles);
        }

        /// <summary>Inserts a new item with the given <paramref name="bounds"/> and <paramref name="payload"/> and returns its stable node handle.</summary>
        public int Insert(in BoundingBox3D bounds, int payload)
        {
            int leaf = AllocateNode();
            _nodes[leaf].Bounds = Fatten(bounds);
            _nodes[leaf].Payload = payload;
            _nodes[leaf].Height = 0;
            _nodes[leaf].Left = NullNode;
            _nodes[leaf].Right = NullNode;
            _leafCount++;
            InsertLeaf(leaf);
            return leaf;
        }

        /// <summary>Removes the item associated with <paramref name="nodeHandle"/> from the tree.</summary>
        public void Remove(int nodeHandle)
        {
            ValidateLeafHandle(nodeHandle);
            RemoveLeaf(nodeHandle);
            FreeNode(nodeHandle);
            _leafCount--;
        }

        /// <summary>
        /// Updates the bounds of an existing item identified by <paramref name="nodeHandle"/>, reusing the same handle.
        /// Returns <c>true</c> if the tree or stored bounds changed.
        /// </summary>
        public bool UpdateBounds(int nodeHandle, in BoundingBox3D newBounds)
        {
            ValidateLeafHandle(nodeHandle);
            ref Node node = ref _nodes[nodeHandle];
            if (ContainsBox(node.Bounds, newBounds))
            {
                if (FattenMargin > 0f || EqualBoxes(node.Bounds, newBounds))
                {
                    return false;
                }
            }

            RemoveLeaf(nodeHandle);
            _nodes[nodeHandle].Bounds = Fatten(newBounds);
            _nodes[nodeHandle].Height = 0;
            _nodes[nodeHandle].Left = NullNode;
            _nodes[nodeHandle].Right = NullNode;
            InsertLeaf(nodeHandle);
            return true;
        }

        /// <summary>Gets the stored (possibly fattened) bounds of the item at <paramref name="nodeHandle"/>.</summary>
        public BoundingBox3D GetBounds(int nodeHandle)
        {
            ValidateLeafHandle(nodeHandle);
            return _nodes[nodeHandle].Bounds;
        }

        /// <summary>Gets the payload stored at <paramref name="nodeHandle"/>.</summary>
        public int GetPayload(int nodeHandle)
        {
            ValidateLeafHandle(nodeHandle);
            return _nodes[nodeHandle].Payload;
        }

        /// <summary>Updates the payload stored at an active <paramref name="nodeHandle"/>.</summary>
        public void SetPayload(int nodeHandle, int payload)
        {
            ValidateLeafHandle(nodeHandle);
            _nodes[nodeHandle].Payload = payload;
        }

        /// <summary>Finds all leaf payloads whose AABB contains <paramref name="point"/>.</summary>
        public void QueryPoint(Vector3 point, List<int> results)
        {
            ListLeafCollector collector = new(results);
            VisitPoint(point, ref collector);
        }

        /// <summary>Visits all leaf payloads whose AABB contains <paramref name="point"/> without heap allocation.</summary>
        public void VisitPoint<TVisitor>(Vector3 point, ref TVisitor visitor)
            where TVisitor : IBvhAabb3DLeafVisitor, allows ref struct
        {
            if (_root == NullNode)
            {
                return;
            }

            int stackCap = GetSafeStackCapacity();
            if (stackCap <= StackAllocThreshold)
            {
                int* stack = stackalloc int[stackCap];
                VisitPointCore(point, stack, ref visitor);
                return;
            }

            int[] rented = ArrayPool<int>.Shared.Rent(stackCap);
            try
            {
                fixed (int* stack = rented)
                {
                    VisitPointCore(point, stack, ref visitor);
                }
            }
            finally
            {
                ArrayPool<int>.Shared.Return(rented);
            }
        }

        /// <summary>Finds all leaf payloads whose AABB overlaps <paramref name="query"/>.</summary>
        public void OverlapAabb(in BoundingBox3D query, List<int> results)
        {
            ListLeafCollector collector = new(results);
            VisitOverlapAabb(query, ref collector);
        }

        /// <summary>Visits all leaf payloads whose AABB overlaps <paramref name="query"/> without heap allocation.</summary>
        public void VisitOverlapAabb<TVisitor>(in BoundingBox3D query, ref TVisitor visitor)
            where TVisitor : IBvhAabb3DLeafVisitor, allows ref struct
        {
            if (_root == NullNode)
            {
                return;
            }

            int stackCap = GetSafeStackCapacity();
            if (stackCap <= StackAllocThreshold)
            {
                int* stack = stackalloc int[stackCap];
                VisitOverlapAabbCore(query, stack, ref visitor);
                return;
            }

            int[] rented = ArrayPool<int>.Shared.Rent(stackCap);
            try
            {
                fixed (int* stack = rented)
                {
                    VisitOverlapAabbCore(query, stack, ref visitor);
                }
            }
            finally
            {
                ArrayPool<int>.Shared.Return(rented);
            }
        }

        /// <summary>Finds the closest leaf payload hit by the ray segment within <c>[0, 1]</c>.</summary>
        public bool RayCastClosest(Vector3 origin, Vector3 displacement, out int hitPayload, out float hitT) =>
            RayCastClosest(origin, displacement, 1f, out hitPayload, out hitT);

        /// <summary>Finds the closest leaf payload hit by the ray segment within <c>[0, maxT]</c>.</summary>
        public bool RayCastClosest(Vector3 origin, Vector3 displacement, float maxT, out int hitPayload, out float hitT)
        {
            hitPayload = -1;
            hitT = 0f;
            if (_root == NullNode || maxT < 0f)
            {
                return false;
            }

            int stackCap = GetSafeStackCapacity();
            if (stackCap <= StackAllocThreshold)
            {
                int* stackNodes = stackalloc int[stackCap];
                float* stackDists = stackalloc float[stackCap];
                return RayCastClosestCore(origin, displacement, maxT, stackNodes, stackDists, out hitPayload, out hitT);
            }

            int[] rentedNodes = ArrayPool<int>.Shared.Rent(stackCap);
            float[] rentedDists = ArrayPool<float>.Shared.Rent(stackCap);
            try
            {
                fixed (int* stackNodes = rentedNodes)
                fixed (float* stackDists = rentedDists)
                {
                    return RayCastClosestCore(origin, displacement, maxT, stackNodes, stackDists, out hitPayload, out hitT);
                }
            }
            finally
            {
                ArrayPool<int>.Shared.Return(rentedNodes);
                ArrayPool<float>.Shared.Return(rentedDists);
            }
        }

        /// <summary>Traverses candidate leaf payloads front-to-back within <c>[0, maxT]</c>, letting <paramref name="visitor"/> tighten <c>bestT</c>.</summary>
        public void VisitRayClosest<TVisitor>(Vector3 origin, Vector3 displacement, float maxT, ref TVisitor visitor)
            where TVisitor : IBvhAabb3DRayClosestVisitor, allows ref struct
        {
            if (_root == NullNode || maxT < 0f)
            {
                return;
            }

            int stackCap = GetSafeStackCapacity();
            if (stackCap <= StackAllocThreshold)
            {
                int* stackNodes = stackalloc int[stackCap];
                float* stackDists = stackalloc float[stackCap];
                VisitRayClosestCore(origin, displacement, maxT, stackNodes, stackDists, ref visitor);
                return;
            }

            int[] rentedNodes = ArrayPool<int>.Shared.Rent(stackCap);
            float[] rentedDists = ArrayPool<float>.Shared.Rent(stackCap);
            try
            {
                fixed (int* stackNodes = rentedNodes)
                fixed (float* stackDists = rentedDists)
                {
                    VisitRayClosestCore(origin, displacement, maxT, stackNodes, stackDists, ref visitor);
                }
            }
            finally
            {
                ArrayPool<int>.Shared.Return(rentedNodes);
                ArrayPool<float>.Shared.Return(rentedDists);
            }
        }

        /// <summary>Returns true if any leaf AABB intersects the ray segment within <c>[0, maxT]</c>.</summary>
        public bool RayCastAny(Vector3 origin, Vector3 displacement, float maxT = 1f)
        {
            if (_root == NullNode || maxT < 0f)
            {
                return false;
            }

            int stackCap = GetSafeStackCapacity();
            if (stackCap <= StackAllocThreshold)
            {
                int* stack = stackalloc int[stackCap];
                return RayCastAnyCore(origin, displacement, maxT, stack);
            }

            int[] rented = ArrayPool<int>.Shared.Rent(stackCap);
            try
            {
                fixed (int* stack = rented)
                {
                    return RayCastAnyCore(origin, displacement, maxT, stack);
                }
            }
            finally
            {
                ArrayPool<int>.Shared.Return(rented);
            }
        }

        /// <summary>Visits all leaf payloads intersecting the ray segment within <c>[0, maxT]</c> until <paramref name="visitor"/> returns false.</summary>
        public void VisitRayAll<TVisitor>(Vector3 origin, Vector3 displacement, float maxT, ref TVisitor visitor)
            where TVisitor : IBvhAabb3DLeafVisitor, allows ref struct
        {
            if (_root == NullNode || maxT < 0f)
            {
                return;
            }

            int stackCap = GetSafeStackCapacity();
            if (stackCap <= StackAllocThreshold)
            {
                int* stack = stackalloc int[stackCap];
                VisitRayAllCore(origin, displacement, maxT, stack, ref visitor);
                return;
            }

            int[] rented = ArrayPool<int>.Shared.Rent(stackCap);
            try
            {
                fixed (int* stack = rented)
                {
                    VisitRayAllCore(origin, displacement, maxT, stack, ref visitor);
                }
            }
            finally
            {
                ArrayPool<int>.Shared.Return(rented);
            }
        }

        private void BulkBuildCore(
            ReadOnlySpan<BoundingBox3D> bounds,
            ReadOnlySpan<int> payloads,
            bool hasExplicitPayloads,
            Span<int> outHandles)
        {
            int n = bounds.Length;
            if (!outHandles.IsEmpty && outHandles.Length < n)
            {
                throw new ArgumentException("outHandles length must be empty or at least bounds.Length.", nameof(outHandles));
            }

            Clear();
            if (n == 0)
            {
                return;
            }

            int requiredNodes = 2 * n - 1;
            if (_nodes.Length < requiredNodes)
            {
                _nodes = new Node[requiredNodes];
            }

            Vector3 sceneMin = new(float.MaxValue);
            Vector3 sceneMax = new(float.MinValue);
            for (int i = 0; i < n; i++)
            {
                Vector3 center = bounds[i].Min + bounds[i].Max;
                sceneMin = Vector3.Min(sceneMin, center);
                sceneMax = Vector3.Max(sceneMax, center);
            }

            ulong[] rentedPairs = ArrayPool<ulong>.Shared.Rent(n);
            BulkItem[] rentedItems = ArrayPool<BulkItem>.Shared.Rent(n);
            try
            {
                Vector3 extent = sceneMax - sceneMin;
                float invX = extent.X > 0f ? 1f / extent.X : 0f;
                float invY = extent.Y > 0f ? 1f / extent.Y : 0f;
                float invZ = extent.Z > 0f ? 1f / extent.Z : 0f;

                for (int i = 0; i < n; i++)
                {
                    Vector3 center = bounds[i].Min + bounds[i].Max;
                    uint code = MortonCode30(
                        (center.X - sceneMin.X) * invX,
                        (center.Y - sceneMin.Y) * invY,
                        (center.Z - sceneMin.Z) * invZ);
                    rentedPairs[i] = ((ulong)code << 32) | (uint)i;
                }

                Array.Sort(rentedPairs, 0, n);
                for (int i = 0; i < n; i++)
                {
                    int srcIdx = (int)(uint)rentedPairs[i];
                    int payload = hasExplicitPayloads ? payloads[srcIdx] : srcIdx;
                    rentedItems[i] = new BulkItem(Fatten(bounds[srcIdx]), srcIdx, payload);
                }

                _nodeCount = 0;
                _leafCount = n;
                _root = BuildMortonRange(rentedPairs, rentedItems, 0, n, 0, outHandles);
                _nodes[_root].ParentOrNextFree = NullNode;
                InitFreeList(_nodeCount, _nodes.Length);
            }
            finally
            {
                ArrayPool<ulong>.Shared.Return(rentedPairs);
                ArrayPool<BulkItem>.Shared.Return(rentedItems);
            }
        }

        private int BuildMortonRange(
            ulong[] sortedPairs,
            BulkItem[] sortedItems,
            int start,
            int end,
            int depth,
            Span<int> outHandles)
        {
            if (end - start == 1)
            {
                int leafIdx = _nodeCount++;
                BulkItem item = sortedItems[start];
                _nodes[leafIdx] = new Node
                {
                    Bounds = item.Bounds,
                    ParentOrNextFree = NullNode,
                    Left = NullNode,
                    Right = NullNode,
                    Height = 0,
                    Payload = item.Payload,
                    IsAllocated = true,
                };
                if (!outHandles.IsEmpty)
                {
                    outHandles[item.OriginalIndex] = leafIdx;
                }
                return leafIdx;
            }

            int split = depth < MaxMortonBuildDepth ? FindMortonSplit(sortedPairs, start, end) : (start + end) / 2;
            int left = BuildMortonRange(sortedPairs, sortedItems, start, split, depth + 1, outHandles);
            int right = BuildMortonRange(sortedPairs, sortedItems, split, end, depth + 1, outHandles);

            int parent = _nodeCount++;
            ref Node pNode = ref _nodes[parent];
            pNode.Left = left;
            pNode.Right = right;
            pNode.ParentOrNextFree = NullNode;
            pNode.Bounds = BoundingBox3D.Merge(_nodes[left].Bounds, _nodes[right].Bounds);
            pNode.Height = 1 + Math.Max(_nodes[left].Height, _nodes[right].Height);
            pNode.Payload = 0;
            pNode.IsAllocated = true;

            _nodes[left].ParentOrNextFree = parent;
            _nodes[right].ParentOrNextFree = parent;
            return parent;
        }

        private static int FindMortonSplit(ulong[] sortedPairs, int start, int end)
        {
            uint first = (uint)(sortedPairs[start] >> 32);
            uint last = (uint)(sortedPairs[end - 1] >> 32);
            uint xor = first ^ last;
            if (xor == 0)
            {
                return (start + end) / 2;
            }

            uint splitBit = 0x80000000u >> BitOperations.LeadingZeroCount(xor);
            int lo = start + 1;
            int hi = end - 1;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (((uint)(sortedPairs[mid] >> 32) & splitBit) == 0)
                {
                    lo = mid + 1;
                }
                else
                {
                    hi = mid;
                }
            }
            return lo;
        }

        private unsafe void VisitPointCore<TVisitor>(Vector3 point, int* stack, ref TVisitor visitor)
            where TVisitor : IBvhAabb3DLeafVisitor, allows ref struct
        {
            int sp = 0;
            stack[sp++] = _root;
            while (sp > 0)
            {
                int idx = stack[--sp];
                ref readonly Node node = ref _nodes[idx];
                if (!ContainsPoint(node.Bounds, point))
                {
                    continue;
                }

                if (node.Left == NullNode)
                {
                    if (!visitor.OnLeaf(node.Payload))
                    {
                        return;
                    }
                }
                else
                {
                    stack[sp++] = node.Right;
                    stack[sp++] = node.Left;
                }
            }
        }

        private unsafe void VisitOverlapAabbCore<TVisitor>(in BoundingBox3D query, int* stack, ref TVisitor visitor)
            where TVisitor : IBvhAabb3DLeafVisitor, allows ref struct
        {
            int sp = 0;
            stack[sp++] = _root;
            while (sp > 0)
            {
                int idx = stack[--sp];
                ref readonly Node node = ref _nodes[idx];
                if (!Overlaps(node.Bounds, query))
                {
                    continue;
                }

                if (node.Left == NullNode)
                {
                    if (!visitor.OnLeaf(node.Payload))
                    {
                        return;
                    }
                }
                else
                {
                    stack[sp++] = node.Right;
                    stack[sp++] = node.Left;
                }
            }
        }

        private unsafe void VisitRayAllCore<TVisitor>(Vector3 origin, Vector3 displacement, float maxT, int* stack, ref TVisitor visitor)
            where TVisitor : IBvhAabb3DLeafVisitor, allows ref struct
        {
            float invX = displacement.X != 0f ? 1f / displacement.X : float.MaxValue;
            float invY = displacement.Y != 0f ? 1f / displacement.Y : float.MaxValue;
            float invZ = displacement.Z != 0f ? 1f / displacement.Z : float.MaxValue;

            int sp = 0;
            stack[sp++] = _root;
            while (sp > 0)
            {
                int idx = stack[--sp];
                ref readonly Node node = ref _nodes[idx];
                if (!TryRaySlab(node.Bounds, origin.X, origin.Y, origin.Z, invX, invY, invZ, maxT, out _))
                {
                    continue;
                }

                if (node.Left == NullNode)
                {
                    if (!visitor.OnLeaf(node.Payload))
                    {
                        return;
                    }
                }
                else
                {
                    stack[sp++] = node.Right;
                    stack[sp++] = node.Left;
                }
            }
        }

        private unsafe bool RayCastAnyCore(Vector3 origin, Vector3 displacement, float maxT, int* stack)
        {
            float invX = displacement.X != 0f ? 1f / displacement.X : float.MaxValue;
            float invY = displacement.Y != 0f ? 1f / displacement.Y : float.MaxValue;
            float invZ = displacement.Z != 0f ? 1f / displacement.Z : float.MaxValue;

            int sp = 0;
            stack[sp++] = _root;
            while (sp > 0)
            {
                int idx = stack[--sp];
                ref readonly Node node = ref _nodes[idx];
                if (!TryRaySlab(node.Bounds, origin.X, origin.Y, origin.Z, invX, invY, invZ, maxT, out _))
                {
                    continue;
                }

                if (node.Left == NullNode)
                {
                    return true;
                }

                stack[sp++] = node.Right;
                stack[sp++] = node.Left;
            }
            return false;
        }

        private unsafe bool RayCastClosestCore(
            Vector3 origin,
            Vector3 displacement,
            float maxT,
            int* stackNodes,
            float* stackDists,
            out int hitPayload,
            out float hitT)
        {
            hitPayload = -1;
            hitT = 0f;

            float invX = displacement.X != 0f ? 1f / displacement.X : float.MaxValue;
            float invY = displacement.Y != 0f ? 1f / displacement.Y : float.MaxValue;
            float invZ = displacement.Z != 0f ? 1f / displacement.Z : float.MaxValue;

            if (!TryRaySlab(_nodes[_root].Bounds, origin.X, origin.Y, origin.Z, invX, invY, invZ, maxT, out float rootDist))
            {
                return false;
            }

            bool hasHit = false;
            float bestT = maxT;
            int bestPayload = -1;

            int sp = 0;
            stackNodes[sp] = _root;
            stackDists[sp] = rootDist;
            sp++;

            while (sp > 0)
            {
                sp--;
                float curDist = stackDists[sp];
                if (curDist > bestT)
                {
                    continue;
                }

                int idx = stackNodes[sp];
                ref readonly Node node = ref _nodes[idx];
                if (node.Left == NullNode)
                {
                    if (curDist < bestT || (!hasHit && curDist <= maxT))
                    {
                        hasHit = true;
                        bestT = curDist;
                        bestPayload = node.Payload;
                    }
                    continue;
                }

                int leftIdx = node.Left;
                int rightIdx = node.Right;
                bool hitLeft = TryRaySlab(_nodes[leftIdx].Bounds, origin.X, origin.Y, origin.Z, invX, invY, invZ, maxT, out float tLeft) && tLeft <= bestT;
                bool hitRight = TryRaySlab(_nodes[rightIdx].Bounds, origin.X, origin.Y, origin.Z, invX, invY, invZ, maxT, out float tRight) && tRight <= bestT;

                if (hitLeft && hitRight)
                {
                    if (tLeft <= tRight)
                    {
                        stackNodes[sp] = rightIdx;
                        stackDists[sp] = tRight;
                        sp++;
                        stackNodes[sp] = leftIdx;
                        stackDists[sp] = tLeft;
                        sp++;
                    }
                    else
                    {
                        stackNodes[sp] = leftIdx;
                        stackDists[sp] = tLeft;
                        sp++;
                        stackNodes[sp] = rightIdx;
                        stackDists[sp] = tRight;
                        sp++;
                    }
                }
                else if (hitLeft)
                {
                    stackNodes[sp] = leftIdx;
                    stackDists[sp] = tLeft;
                    sp++;
                }
                else if (hitRight)
                {
                    stackNodes[sp] = rightIdx;
                    stackDists[sp] = tRight;
                    sp++;
                }
            }

            if (hasHit)
            {
                hitPayload = bestPayload;
                hitT = bestT;
                return true;
            }
            return false;
        }

        private unsafe void VisitRayClosestCore<TVisitor>(
            Vector3 origin,
            Vector3 displacement,
            float maxT,
            int* stackNodes,
            float* stackDists,
            ref TVisitor visitor)
            where TVisitor : IBvhAabb3DRayClosestVisitor, allows ref struct
        {
            float invX = displacement.X != 0f ? 1f / displacement.X : float.MaxValue;
            float invY = displacement.Y != 0f ? 1f / displacement.Y : float.MaxValue;
            float invZ = displacement.Z != 0f ? 1f / displacement.Z : float.MaxValue;

            if (!TryRaySlab(_nodes[_root].Bounds, origin.X, origin.Y, origin.Z, invX, invY, invZ, maxT, out float rootDist))
            {
                return;
            }

            float bestT = maxT;
            int sp = 0;
            stackNodes[sp] = _root;
            stackDists[sp] = rootDist;
            sp++;

            while (sp > 0)
            {
                sp--;
                float curDist = stackDists[sp];
                if (curDist > bestT)
                {
                    continue;
                }

                int idx = stackNodes[sp];
                ref readonly Node node = ref _nodes[idx];
                if (node.Left == NullNode)
                {
                    float nextBest = visitor.OnLeaf(node.Payload, curDist, bestT);
                    if (nextBest < bestT)
                    {
                        bestT = nextBest;
                    }
                    continue;
                }

                int leftIdx = node.Left;
                int rightIdx = node.Right;
                bool hitLeft = TryRaySlab(_nodes[leftIdx].Bounds, origin.X, origin.Y, origin.Z, invX, invY, invZ, maxT, out float tLeft) && tLeft <= bestT;
                bool hitRight = TryRaySlab(_nodes[rightIdx].Bounds, origin.X, origin.Y, origin.Z, invX, invY, invZ, maxT, out float tRight) && tRight <= bestT;

                if (hitLeft && hitRight)
                {
                    if (tLeft <= tRight)
                    {
                        stackNodes[sp] = rightIdx;
                        stackDists[sp] = tRight;
                        sp++;
                        stackNodes[sp] = leftIdx;
                        stackDists[sp] = tLeft;
                        sp++;
                    }
                    else
                    {
                        stackNodes[sp] = leftIdx;
                        stackDists[sp] = tLeft;
                        sp++;
                        stackNodes[sp] = rightIdx;
                        stackDists[sp] = tRight;
                        sp++;
                    }
                }
                else if (hitLeft)
                {
                    stackNodes[sp] = leftIdx;
                    stackDists[sp] = tLeft;
                    sp++;
                }
                else if (hitRight)
                {
                    stackNodes[sp] = rightIdx;
                    stackDists[sp] = tRight;
                    sp++;
                }
            }
        }

        private void InsertLeaf(int leaf)
        {
            if (_root == NullNode)
            {
                _root = leaf;
                _nodes[_root].ParentOrNextFree = NullNode;
                return;
            }

            BoundingBox3D leafBox = _nodes[leaf].Bounds;
            int index = _root;
            while (_nodes[index].Left != NullNode)
            {
                int left = _nodes[index].Left;
                int right = _nodes[index].Right;

                float area = HalfSurfaceArea(_nodes[index].Bounds);
                BoundingBox3D combined = BoundingBox3D.Merge(_nodes[index].Bounds, leafBox);
                float combinedArea = HalfSurfaceArea(combined);

                float costParent = 2f * combinedArea;
                float inheritanceCost = 2f * (combinedArea - area);

                float costLeft = ComputeChildCost(_nodes[left], leafBox, inheritanceCost);
                float costRight = ComputeChildCost(_nodes[right], leafBox, inheritanceCost);

                if (costParent < costLeft && costParent < costRight)
                {
                    break;
                }

                index = costLeft < costRight ? left : right;
            }

            int sibling = index;
            int oldParent = _nodes[sibling].ParentOrNextFree;
            int newParent = AllocateNode();
            _nodes[newParent].ParentOrNextFree = oldParent;
            _nodes[newParent].Bounds = BoundingBox3D.Merge(leafBox, _nodes[sibling].Bounds);
            _nodes[newParent].Height = _nodes[sibling].Height + 1;
            _nodes[newParent].Left = sibling;
            _nodes[newParent].Right = leaf;
            _nodes[sibling].ParentOrNextFree = newParent;
            _nodes[leaf].ParentOrNextFree = newParent;

            if (oldParent != NullNode)
            {
                if (_nodes[oldParent].Left == sibling)
                {
                    _nodes[oldParent].Left = newParent;
                }
                else
                {
                    _nodes[oldParent].Right = newParent;
                }
            }
            else
            {
                _root = newParent;
            }

            FixUpwards(_nodes[leaf].ParentOrNextFree);
        }

        private void RemoveLeaf(int leaf)
        {
            if (leaf == _root)
            {
                _root = NullNode;
                return;
            }

            int parent = _nodes[leaf].ParentOrNextFree;
            int grandParent = _nodes[parent].ParentOrNextFree;
            int sibling = _nodes[parent].Left == leaf ? _nodes[parent].Right : _nodes[parent].Left;

            if (grandParent != NullNode)
            {
                if (_nodes[grandParent].Left == parent)
                {
                    _nodes[grandParent].Left = sibling;
                }
                else
                {
                    _nodes[grandParent].Right = sibling;
                }
                _nodes[sibling].ParentOrNextFree = grandParent;
                FreeNode(parent);
                FixUpwards(grandParent);
            }
            else
            {
                _root = sibling;
                _nodes[sibling].ParentOrNextFree = NullNode;
                FreeNode(parent);
            }
        }

        private void FixUpwards(int index)
        {
            while (index != NullNode)
            {
                index = Balance(index);
                int left = _nodes[index].Left;
                int right = _nodes[index].Right;
                _nodes[index].Height = 1 + Math.Max(_nodes[left].Height, _nodes[right].Height);
                _nodes[index].Bounds = BoundingBox3D.Merge(_nodes[left].Bounds, _nodes[right].Bounds);
                index = _nodes[index].ParentOrNextFree;
            }
        }

        private int Balance(int a)
        {
            if (_nodes[a].Left == NullNode || _nodes[a].Height < 2)
            {
                return a;
            }

            int b = _nodes[a].Left;
            int c = _nodes[a].Right;
            int balance = _nodes[c].Height - _nodes[b].Height;

            if (balance > 1)
            {
                int f = _nodes[c].Left;
                int g = _nodes[c].Right;
                _nodes[c].Left = a;
                _nodes[c].ParentOrNextFree = _nodes[a].ParentOrNextFree;
                _nodes[a].ParentOrNextFree = c;

                if (_nodes[c].ParentOrNextFree != NullNode)
                {
                    if (_nodes[_nodes[c].ParentOrNextFree].Left == a)
                    {
                        _nodes[_nodes[c].ParentOrNextFree].Left = c;
                    }
                    else
                    {
                        _nodes[_nodes[c].ParentOrNextFree].Right = c;
                    }
                }
                else
                {
                    _root = c;
                }

                if (_nodes[f].Height > _nodes[g].Height)
                {
                    _nodes[c].Right = f;
                    _nodes[a].Right = g;
                    _nodes[g].ParentOrNextFree = a;
                    _nodes[a].Bounds = BoundingBox3D.Merge(_nodes[b].Bounds, _nodes[g].Bounds);
                    _nodes[c].Bounds = BoundingBox3D.Merge(_nodes[a].Bounds, _nodes[f].Bounds);
                    _nodes[a].Height = 1 + Math.Max(_nodes[b].Height, _nodes[g].Height);
                    _nodes[c].Height = 1 + Math.Max(_nodes[a].Height, _nodes[f].Height);
                }
                else
                {
                    _nodes[c].Right = g;
                    _nodes[a].Right = f;
                    _nodes[f].ParentOrNextFree = a;
                    _nodes[a].Bounds = BoundingBox3D.Merge(_nodes[b].Bounds, _nodes[f].Bounds);
                    _nodes[c].Bounds = BoundingBox3D.Merge(_nodes[a].Bounds, _nodes[g].Bounds);
                    _nodes[a].Height = 1 + Math.Max(_nodes[b].Height, _nodes[f].Height);
                    _nodes[c].Height = 1 + Math.Max(_nodes[a].Height, _nodes[g].Height);
                }
                return c;
            }

            if (balance < -1)
            {
                int d = _nodes[b].Left;
                int e = _nodes[b].Right;
                _nodes[b].Left = a;
                _nodes[b].ParentOrNextFree = _nodes[a].ParentOrNextFree;
                _nodes[a].ParentOrNextFree = b;

                if (_nodes[b].ParentOrNextFree != NullNode)
                {
                    if (_nodes[_nodes[b].ParentOrNextFree].Left == a)
                    {
                        _nodes[_nodes[b].ParentOrNextFree].Left = b;
                    }
                    else
                    {
                        _nodes[_nodes[b].ParentOrNextFree].Right = b;
                    }
                }
                else
                {
                    _root = b;
                }

                if (_nodes[d].Height > _nodes[e].Height)
                {
                    _nodes[b].Right = d;
                    _nodes[a].Left = e;
                    _nodes[e].ParentOrNextFree = a;
                    _nodes[a].Bounds = BoundingBox3D.Merge(_nodes[c].Bounds, _nodes[e].Bounds);
                    _nodes[b].Bounds = BoundingBox3D.Merge(_nodes[a].Bounds, _nodes[d].Bounds);
                    _nodes[a].Height = 1 + Math.Max(_nodes[c].Height, _nodes[e].Height);
                    _nodes[b].Height = 1 + Math.Max(_nodes[a].Height, _nodes[d].Height);
                }
                else
                {
                    _nodes[b].Right = e;
                    _nodes[a].Left = d;
                    _nodes[d].ParentOrNextFree = a;
                    _nodes[a].Bounds = BoundingBox3D.Merge(_nodes[c].Bounds, _nodes[d].Bounds);
                    _nodes[b].Bounds = BoundingBox3D.Merge(_nodes[a].Bounds, _nodes[e].Bounds);
                    _nodes[a].Height = 1 + Math.Max(_nodes[c].Height, _nodes[d].Height);
                    _nodes[b].Height = 1 + Math.Max(_nodes[a].Height, _nodes[e].Height);
                }
                return b;
            }

            return a;
        }

        private int AllocateNode()
        {
            if (_freeList == NullNode)
            {
                int oldCap = _nodes.Length;
                int newCap = oldCap * 2;
                Array.Resize(ref _nodes, newCap);
                InitFreeList(oldCap, newCap);
            }

            int idx = _freeList;
            _freeList = _nodes[idx].ParentOrNextFree;
            _nodes[idx].ParentOrNextFree = NullNode;
            _nodes[idx].Left = NullNode;
            _nodes[idx].Right = NullNode;
            _nodes[idx].Height = 0;
            _nodes[idx].IsAllocated = true;
            _nodeCount++;
            return idx;
        }

        private void FreeNode(int idx)
        {
            _nodes[idx].IsAllocated = false;
            _nodes[idx].ParentOrNextFree = _freeList;
            _nodes[idx].Height = -1;
            _freeList = idx;
            _nodeCount--;
        }

        private void InitFreeList(int start, int end)
        {
            for (int i = start; i < end - 1; i++)
            {
                _nodes[i].ParentOrNextFree = i + 1;
                _nodes[i].Height = -1;
                _nodes[i].IsAllocated = false;
            }
            if (end > start)
            {
                _nodes[end - 1].ParentOrNextFree = NullNode;
                _nodes[end - 1].Height = -1;
                _nodes[end - 1].IsAllocated = false;
            }
            _freeList = start < end ? start : NullNode;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int GetSafeStackCapacity()
        {
            if (_root == NullNode)
            {
                return 4;
            }
            return Math.Max(4, _nodes[_root].Height + 4);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ValidateLeafHandle(int nodeHandle)
        {
            if ((uint)nodeHandle >= (uint)_nodes.Length || !_nodes[nodeHandle].IsAllocated || _nodes[nodeHandle].Left != NullNode)
            {
                throw new ArgumentOutOfRangeException(nameof(nodeHandle), "Invalid or freed dynamic BVH leaf handle.");
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private BoundingBox3D Fatten(in BoundingBox3D bounds)
        {
            if (FattenMargin <= 0f)
            {
                return bounds;
            }
            Vector3 margin = new(FattenMargin);
            return new BoundingBox3D(bounds.Min - margin, bounds.Max + margin);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float HalfSurfaceArea(in BoundingBox3D b)
        {
            Vector3 d = b.Max - b.Min;
            return d.X * d.Y + d.Y * d.Z + d.Z * d.X;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float ComputeChildCost(in Node child, in BoundingBox3D leafBox, float inheritanceCost)
        {
            BoundingBox3D merged = BoundingBox3D.Merge(child.Bounds, leafBox);
            float area = HalfSurfaceArea(merged);
            return child.Left == NullNode
                ? area + inheritanceCost
                : (area - HalfSurfaceArea(child.Bounds)) + inheritanceCost;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool ContainsBox(in BoundingBox3D outer, in BoundingBox3D inner) =>
            outer.Min.X <= inner.Min.X && outer.Min.Y <= inner.Min.Y && outer.Min.Z <= inner.Min.Z &&
            outer.Max.X >= inner.Max.X && outer.Max.Y >= inner.Max.Y && outer.Max.Z >= inner.Max.Z;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool EqualBoxes(in BoundingBox3D a, in BoundingBox3D b) =>
            a.Min.X == b.Min.X && a.Min.Y == b.Min.Y && a.Min.Z == b.Min.Z &&
            a.Max.X == b.Max.X && a.Max.Y == b.Max.Y && a.Max.Z == b.Max.Z;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool ContainsPoint(in BoundingBox3D box, Vector3 point) =>
            box.Min.X <= point.X && point.X <= box.Max.X &&
            box.Min.Y <= point.Y && point.Y <= box.Max.Y &&
            box.Min.Z <= point.Z && point.Z <= box.Max.Z;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool Overlaps(in BoundingBox3D a, in BoundingBox3D b) =>
            a.Min.X <= b.Max.X && b.Min.X <= a.Max.X &&
            a.Min.Y <= b.Max.Y && b.Min.Y <= a.Max.Y &&
            a.Min.Z <= b.Max.Z && b.Min.Z <= a.Max.Z;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool TryRaySlab(
            in BoundingBox3D box,
            float orgX,
            float orgY,
            float orgZ,
            float invX,
            float invY,
            float invZ,
            float maxT,
            out float rawTMin)
        {
            float tx1 = (box.Min.X - orgX) * invX;
            float tx2 = (box.Max.X - orgX) * invX;
            float tMin = tx1 < tx2 ? tx1 : tx2;
            float tMax = tx1 > tx2 ? tx1 : tx2;

            float ty1 = (box.Min.Y - orgY) * invY;
            float ty2 = (box.Max.Y - orgY) * invY;
            float tyMin = ty1 < ty2 ? ty1 : ty2;
            float tyMax = ty1 > ty2 ? ty1 : ty2;

            if (tyMin > tMin) tMin = tyMin;
            if (tyMax < tMax) tMax = tyMax;

            float tz1 = (box.Min.Z - orgZ) * invZ;
            float tz2 = (box.Max.Z - orgZ) * invZ;
            float tzMin = tz1 < tz2 ? tz1 : tz2;
            float tzMax = tz1 > tz2 ? tz1 : tz2;

            if (tzMin > tMin) tMin = tzMin;
            if (tzMax < tMax) tMax = tzMax;

            rawTMin = tMin;
            float clampedMin = tMin > 0f ? tMin : 0f;
            float clampedMax = tMax < maxT ? tMax : maxT;
            return clampedMin <= clampedMax;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint MortonCode30(float x, float y, float z)
        {
            const float scale = (1 << 10) - 1;
            uint xi = (uint)Math.Min(Math.Max(x * scale, 0f), scale);
            uint yi = (uint)Math.Min(Math.Max(y * scale, 0f), scale);
            uint zi = (uint)Math.Min(Math.Max(z * scale, 0f), scale);
            return (Part1By2(xi) << 2) | (Part1By2(yi) << 1) | Part1By2(zi);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint Part1By2(uint v)
        {
            v &= 0x3FFu;
            v = (v | (v << 16)) & 0x030000FFu;
            v = (v | (v << 8)) & 0x0300F00Fu;
            v = (v | (v << 4)) & 0x030C30C3u;
            v = (v | (v << 2)) & 0x09249249u;
            return v;
        }
    }
}
