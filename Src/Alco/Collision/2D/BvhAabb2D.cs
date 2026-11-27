using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;

namespace Alco
{
    /// <summary>
    /// A static 4-wide SIMD bounding volume hierarchy over 2D axis-aligned bounding boxes,
    /// built with a 20-bit or 30-bit Morton LBVH via <see cref="BvhAabb2DBuilder"/> and stored
    /// in exact-sized unmanaged node and leaf buffers.
    /// </summary>
    public sealed unsafe class BvhAabb2D : IDisposable
    {
        /// <summary>Fanout of every internal node.</summary>
        public const int Width = 4;

        /// <summary>Maximum number of items packed into a single leaf block.</summary>
        public const int MaxLeafItems = 4;

        /// <summary>Sentinel child reference representing an empty slot or empty tree.</summary>
        public const int EmptyRef = int.MinValue;

        /// <summary>Sentinel payload written into unused leaf lanes.</summary>
        public const int EmptyLeafPayload = int.MinValue;

        private const int StackAllocThreshold = 512;

        /// <summary>
        /// A 4-wide SoA internal BVH node (80 bytes).
        /// </summary>
        public struct Node
        {
            /// <summary>Minimum X coordinates of the 4 child boxes.</summary>
            public Vector128<float> LowerX;

            /// <summary>Maximum X coordinates of the 4 child boxes.</summary>
            public Vector128<float> UpperX;

            /// <summary>Minimum Y coordinates of the 4 child boxes.</summary>
            public Vector128<float> LowerY;

            /// <summary>Maximum Y coordinates of the 4 child boxes.</summary>
            public Vector128<float> UpperY;

            /// <summary>Tagged child references: non-negative for internal node index, negative for bitwise-inverted leaf index, or <see cref="EmptyRef"/>.</summary>
            public fixed int Children[4];
        }

        /// <summary>
        /// A 4-wide SoA leaf block storing up to 4 item AABBs and payloads (80 bytes).
        /// </summary>
        public struct Leaf
        {
            /// <summary>Minimum X coordinates of the 4 leaf boxes.</summary>
            public Vector128<float> LowerX;

            /// <summary>Maximum X coordinates of the 4 leaf boxes.</summary>
            public Vector128<float> UpperX;

            /// <summary>Minimum Y coordinates of the 4 leaf boxes.</summary>
            public Vector128<float> LowerY;

            /// <summary>Maximum Y coordinates of the 4 leaf boxes.</summary>
            public Vector128<float> UpperY;

            /// <summary>Item payloads or <see cref="EmptyLeafPayload"/> for unused lanes.</summary>
            public fixed int Indices[4];
        }

        private struct StackEntry
        {
            public int Child;
            public float Dist;
        }

        private struct ListLeafCollector : IBvhAabb2DLeafVisitor
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

        private Node* _nodes;
        private Leaf* _leaves;
        private int _nodeCapacity;
        private int _leafCapacity;
        private int _rootRef = EmptyRef;
        private int _nodeCount;
        private int _leafCount;
        private int _treeDepth;
        private BvhAabb2DBuilder? _defaultBuilder;
        private bool _isDisposed;

        /// <summary>Current number of internal nodes in the tree.</summary>
        public int NodeCount => _nodeCount;

        /// <summary>Current number of leaf blocks in the tree.</summary>
        public int LeafCount => _leafCount;

        /// <summary>Maximum depth of the built tree (0 when empty).</summary>
        public int TreeDepth => _treeDepth;

        /// <summary>Exact native unmanaged bytes currently allocated for internal nodes and leaf blocks.</summary>
        public long NativeMemoryBytes => (long)_nodeCapacity * sizeof(Node) + (long)_leafCapacity * sizeof(Leaf);

        /// <summary>
        /// Builds the BVH over <paramref name="bounds"/> using an internal scratch builder and 30-bit Morton codes.
        /// Not thread-safe against concurrent queries.
        /// </summary>
        public void Build(ReadOnlySpan<BoundingBox2D> bounds) => Build(bounds, use30BitMorton: true);

        /// <summary>
        /// Builds the BVH over <paramref name="bounds"/> using an internal scratch builder and the selected Morton bit width.
        /// Not thread-safe against concurrent queries.
        /// </summary>
        public void Build(ReadOnlySpan<BoundingBox2D> bounds, bool use30BitMorton)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            if (bounds.IsEmpty)
            {
                Clear();
                return;
            }

            _defaultBuilder ??= new BvhAabb2DBuilder();
            Build(bounds, _defaultBuilder, use30BitMorton);
        }

        /// <summary>
        /// Builds the BVH over <paramref name="bounds"/> with sequential payloads <c>0..bounds.Length-1</c>
        /// using the caller-provided shared <paramref name="builder"/>.
        /// Not thread-safe against concurrent queries.
        /// </summary>
        public void Build(ReadOnlySpan<BoundingBox2D> bounds, BvhAabb2DBuilder builder, bool use30BitMorton = true)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            ArgumentNullException.ThrowIfNull(builder);
            if (bounds.IsEmpty)
            {
                Clear();
                return;
            }

            BvhAabb2DBuildResult result = builder.Build(bounds, use30BitMorton, out ReadOnlySpan<Node> builtNodes, out ReadOnlySpan<Leaf> builtLeaves);
            CommitBuild(result, builtNodes, builtLeaves);
        }

        /// <summary>
        /// Builds the BVH over <paramref name="bounds"/> and explicit <paramref name="payloads"/>
        /// using the caller-provided shared <paramref name="builder"/>.
        /// Not thread-safe against concurrent queries.
        /// </summary>
        public void Build(ReadOnlySpan<BoundingBox2D> bounds, ReadOnlySpan<int> payloads, BvhAabb2DBuilder builder, bool use30BitMorton = true)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            ArgumentNullException.ThrowIfNull(builder);
            if (bounds.IsEmpty)
            {
                Clear();
                return;
            }

            BvhAabb2DBuildResult result = builder.Build(bounds, payloads, use30BitMorton, out ReadOnlySpan<Node> builtNodes, out ReadOnlySpan<Leaf> builtLeaves);
            CommitBuild(result, builtNodes, builtLeaves);
        }

        /// <summary>
        /// Releases native node and leaf buffers and resets the tree to empty.
        /// </summary>
        public void Clear()
        {
            if (_nodes != null)
            {
                MemoryUtility.Free(_nodes);
                _nodes = null;
                _nodeCapacity = 0;
            }
            if (_leaves != null)
            {
                MemoryUtility.Free(_leaves);
                _leaves = null;
                _leafCapacity = 0;
            }

            _rootRef = EmptyRef;
            _nodeCount = 0;
            _leafCount = 0;
            _treeDepth = 0;
        }

        private void CommitBuild(BvhAabb2DBuildResult result, ReadOnlySpan<Node> builtNodes, ReadOnlySpan<Leaf> builtLeaves)
        {
            int nodeCount = result.NodeCount;
            int leafCount = result.LeafCount;

            if (_nodeCapacity != nodeCount)
            {
                if (_nodes != null)
                {
                    MemoryUtility.Free(_nodes);
                    _nodes = null;
                }
                _nodeCapacity = nodeCount;
                if (nodeCount > 0)
                {
                    _nodes = MemoryUtility.Alloc<Node>(nodeCount);
                }
            }

            if (_leafCapacity != leafCount)
            {
                if (_leaves != null)
                {
                    MemoryUtility.Free(_leaves);
                    _leaves = null;
                }
                _leafCapacity = leafCount;
                if (leafCount > 0)
                {
                    _leaves = MemoryUtility.Alloc<Leaf>(leafCount);
                }
            }

            if (nodeCount > 0)
            {
                builtNodes.CopyTo(new Span<Node>(_nodes, nodeCount));
            }
            if (leafCount > 0)
            {
                builtLeaves.CopyTo(new Span<Leaf>(_leaves, leafCount));
            }

            _rootRef = result.RootRef;
            _nodeCount = nodeCount;
            _leafCount = leafCount;
            _treeDepth = result.TreeDepth;
        }

        // ════════════════════════════════════════════════════════════
        //  Queries (thread-safe for concurrent reads after Build)
        // ════════════════════════════════════════════════════════════

        /// <summary>
        /// Finds the closest leaf item whose AABB is entered by the ray segment <c>origin + displacement * t</c> for <c>t</c> in <c>[0, 1]</c>,
        /// returning the raw slab entry fraction as <paramref name="hitT"/> (which may be negative when the origin starts inside a box).
        /// Thread-safe for concurrent reads after building.
        /// </summary>
        public bool RayCastClosest(Vector2 origin, Vector2 displacement, out int hitIndex, out float hitT) =>
            RayCastClosest(origin, displacement, 1f, out hitIndex, out hitT);

        /// <summary>
        /// Finds the closest leaf item whose AABB is entered by the ray segment <c>origin + displacement * t</c> for <c>t</c> in <c>[0, maxT]</c>,
        /// returning the raw slab entry fraction as <paramref name="hitT"/> (which may be negative when the origin starts inside a box).
        /// Thread-safe for concurrent reads after building.
        /// </summary>
        public bool RayCastClosest(Vector2 origin, Vector2 displacement, float maxT, out int hitIndex, out float hitT)
        {
            hitIndex = -1;
            hitT = 0f;
            if (_rootRef == EmptyRef || maxT < 0f)
            {
                return false;
            }

            int stackCap = _treeDepth * (Width - 1) + Width;
            if (stackCap <= StackAllocThreshold)
            {
                StackEntry* stack = stackalloc StackEntry[stackCap];
                return RayCastClosestCore(origin, displacement, maxT, stack, out hitIndex, out hitT);
            }

            StackEntry* rented = MemoryUtility.Alloc<StackEntry>(stackCap);
            try
            {
                return RayCastClosestCore(origin, displacement, maxT, rented, out hitIndex, out hitT);
            }
            finally
            {
                MemoryUtility.Free(rented);
            }
        }

        /// <summary>
        /// Traverses candidate leaf payloads front-to-back along the ray segment up to <paramref name="maxT"/>,
        /// allowing <paramref name="visitor"/> to tighten the active distance bound dynamically via narrowphase hits.
        /// Thread-safe for concurrent reads after building.
        /// </summary>
        public void VisitRayClosest<TVisitor>(Vector2 origin, Vector2 displacement, float maxT, ref TVisitor visitor)
            where TVisitor : IBvhAabb2DRayClosestVisitor, allows ref struct =>
            VisitRayClosest(origin, displacement, maxT, maxT, ref visitor);

        /// <summary>
        /// Traverses candidate leaf payloads front-to-back along the ray segment <c>[0, segmentMaxT]</c> with an initial
        /// best fraction bound <paramref name="initialBestT"/> (which may be negative when continuing across chunks
        /// after an origin-inside slab hit).
        /// </summary>
        public void VisitRayClosest<TVisitor>(Vector2 origin, Vector2 displacement, float segmentMaxT, float initialBestT, ref TVisitor visitor)
            where TVisitor : IBvhAabb2DRayClosestVisitor, allows ref struct
        {
            if (_rootRef == EmptyRef || segmentMaxT < 0f)
            {
                return;
            }

            int stackCap = _treeDepth * (Width - 1) + Width;
            if (stackCap <= StackAllocThreshold)
            {
                StackEntry* stack = stackalloc StackEntry[stackCap];
                VisitRayClosestCore(origin, displacement, segmentMaxT, initialBestT, stack, ref visitor);
                return;
            }

            StackEntry* rented = MemoryUtility.Alloc<StackEntry>(stackCap);
            try
            {
                VisitRayClosestCore(origin, displacement, segmentMaxT, initialBestT, rented, ref visitor);
            }
            finally
            {
                MemoryUtility.Free(rented);
            }
        }

        /// <summary>
        /// Casts a ray segment against the BVH and returns on the first intersecting leaf AABB in <c>[0, 1]</c>.
        /// Thread-safe for concurrent reads after building.
        /// </summary>
        public bool RayCastAny(Vector2 origin, Vector2 displacement) => RayCastAny(origin, displacement, 1f);

        /// <summary>
        /// Casts a ray segment against the BVH and returns on the first intersecting leaf AABB in <c>[0, maxT]</c>.
        /// Thread-safe for concurrent reads after building.
        /// </summary>
        public bool RayCastAny(Vector2 origin, Vector2 displacement, float maxT)
        {
            if (_rootRef == EmptyRef || maxT < 0f)
            {
                return false;
            }

            int stackCap = _treeDepth * (Width - 1) + Width;
            if (stackCap <= StackAllocThreshold)
            {
                int* stack = stackalloc int[stackCap];
                return RayCastAnyCore(origin, displacement, maxT, stack);
            }

            int* rented = MemoryUtility.Alloc<int>(stackCap);
            try
            {
                return RayCastAnyCore(origin, displacement, maxT, rented);
            }
            finally
            {
                MemoryUtility.Free(rented);
            }
        }

        /// <summary>
        /// Visits all leaf payloads whose AABB intersects the ray segment <c>[0, maxT]</c> until <paramref name="visitor"/> returns <c>false</c>.
        /// Thread-safe for concurrent reads after building.
        /// </summary>
        public void VisitRayAll<TVisitor>(Vector2 origin, Vector2 displacement, float maxT, ref TVisitor visitor)
            where TVisitor : IBvhAabb2DLeafVisitor, allows ref struct
        {
            if (_rootRef == EmptyRef || maxT < 0f)
            {
                return;
            }

            int stackCap = _treeDepth * (Width - 1) + Width;
            if (stackCap <= StackAllocThreshold)
            {
                int* stack = stackalloc int[stackCap];
                VisitRayAllCore(origin, displacement, maxT, stack, ref visitor);
                return;
            }

            int* rented = MemoryUtility.Alloc<int>(stackCap);
            try
            {
                VisitRayAllCore(origin, displacement, maxT, rented, ref visitor);
            }
            finally
            {
                MemoryUtility.Free(rented);
            }
        }

        /// <summary>
        /// Finds all leaf items whose AABB overlaps <paramref name="query"/> and appends their payloads to <paramref name="results"/>.
        /// Thread-safe for concurrent reads after building.
        /// </summary>
        public void OverlapAabb(in BoundingBox2D query, List<int> results)
        {
            ListLeafCollector collector = new(results);
            VisitOverlapAabb(query, ref collector);
        }

        /// <summary>
        /// Visits all leaf payloads whose AABB overlaps <paramref name="query"/> without heap allocation.
        /// Thread-safe for concurrent reads after building.
        /// </summary>
        public void VisitOverlapAabb<TVisitor>(in BoundingBox2D query, ref TVisitor visitor)
            where TVisitor : IBvhAabb2DLeafVisitor, allows ref struct
        {
            if (_rootRef == EmptyRef)
            {
                return;
            }

            int stackCap = _treeDepth * (Width - 1) + Width;
            if (stackCap <= StackAllocThreshold)
            {
                int* stack = stackalloc int[stackCap];
                VisitOverlapAabbCore(query, stack, ref visitor);
                return;
            }

            int* rented = MemoryUtility.Alloc<int>(stackCap);
            try
            {
                VisitOverlapAabbCore(query, rented, ref visitor);
            }
            finally
            {
                MemoryUtility.Free(rented);
            }
        }

        /// <summary>
        /// Finds all leaf items whose AABB contains <paramref name="point"/> and appends their payloads to <paramref name="results"/>.
        /// Thread-safe for concurrent reads after building.
        /// </summary>
        public void QueryPoint(Vector2 point, List<int> results)
        {
            ListLeafCollector collector = new(results);
            VisitPoint(point, ref collector);
        }

        /// <summary>
        /// Visits all leaf payloads whose AABB contains <paramref name="point"/> without heap allocation.
        /// Thread-safe for concurrent reads after building.
        /// </summary>
        public void VisitPoint<TVisitor>(Vector2 point, ref TVisitor visitor)
            where TVisitor : IBvhAabb2DLeafVisitor, allows ref struct
        {
            if (_rootRef == EmptyRef)
            {
                return;
            }

            int stackCap = _treeDepth * (Width - 1) + Width;
            if (stackCap <= StackAllocThreshold)
            {
                int* stack = stackalloc int[stackCap];
                VisitPointCore(point, stack, ref visitor);
                return;
            }

            int* rented = MemoryUtility.Alloc<int>(stackCap);
            try
            {
                VisitPointCore(point, rented, ref visitor);
            }
            finally
            {
                MemoryUtility.Free(rented);
            }
        }

        private bool RayCastClosestCore(Vector2 origin, Vector2 displacement, float maxT, StackEntry* stack, out int hitIndex, out float hitT)
        {
            hitIndex = -1;
            hitT = 0f;

            float invX = displacement.X != 0f ? 1f / displacement.X : float.MaxValue;
            float invY = displacement.Y != 0f ? 1f / displacement.Y : float.MaxValue;
            bool posX = invX >= 0f;
            bool posY = invY >= 0f;
            Vector128<float> orgX = Vector128.Create(origin.X);
            Vector128<float> orgY = Vector128.Create(origin.Y);
            Vector128<float> rdirX = Vector128.Create(invX);
            Vector128<float> rdirY = Vector128.Create(invY);
            Vector128<float> zero = Vector128<float>.Zero;
            Vector128<float> maxTVec = Vector128.Create(maxT);

            Node* nodes = _nodes;
            Leaf* leaves = _leaves;

            bool hasHit = false;
            float bestT = maxT;
            int bestIndex = EmptyLeafPayload;

            int* orderChild = stackalloc int[Width];
            float* orderDist = stackalloc float[Width];
            int sp = 0;

            int cur = _rootRef;
            float curDist = 0f;

            while (true)
            {
                while (true)
                {
                    if (curDist > bestT)
                    {
                        break;
                    }

                    if (cur < 0)
                    {
                        Leaf* leaf = leaves + ~cur;
                        Vector128<float> t0 = Vector128.Multiply(Vector128.Subtract(leaf->LowerX, orgX), rdirX);
                        Vector128<float> t1 = Vector128.Multiply(Vector128.Subtract(leaf->UpperX, orgX), rdirX);
                        Vector128<float> tMin = posX ? t0 : t1;
                        Vector128<float> tMax = posX ? t1 : t0;
                        t0 = Vector128.Multiply(Vector128.Subtract(leaf->LowerY, orgY), rdirY);
                        t1 = Vector128.Multiply(Vector128.Subtract(leaf->UpperY, orgY), rdirY);
                        tMin = Vector128.Max(tMin, posY ? t0 : t1);
                        tMax = Vector128.Min(tMax, posY ? t1 : t0);
                        Vector128<float> leafSeg = Vector128.LessThanOrEqual(Vector128.Max(tMin, zero), Vector128.Min(tMax, maxTVec));
                        int leafMask = (int)Vector128.BitwiseAnd(leafSeg, Vector128.LessThanOrEqual(tMin, Vector128.Create(bestT))).ExtractMostSignificantBits();
                        if (leafMask != 0)
                        {
                            for (int i = 0; i < Width; i++)
                            {
                                if ((leafMask & (1 << i)) != 0)
                                {
                                    int payload = leaf->Indices[i];
                                    if (payload != EmptyLeafPayload)
                                    {
                                        float t = tMin.GetElement(i);
                                        if (t < bestT || (!hasHit && t <= maxT))
                                        {
                                            hasHit = true;
                                            bestT = t;
                                            bestIndex = payload;
                                        }
                                    }
                                }
                            }
                        }
                        break;
                    }

                    Node* node = nodes + cur;
                    Vector128<float> s0 = Vector128.Multiply(Vector128.Subtract(node->LowerX, orgX), rdirX);
                    Vector128<float> s1 = Vector128.Multiply(Vector128.Subtract(node->UpperX, orgX), rdirX);
                    Vector128<float> sMin = posX ? s0 : s1;
                    Vector128<float> sMax = posX ? s1 : s0;
                    s0 = Vector128.Multiply(Vector128.Subtract(node->LowerY, orgY), rdirY);
                    s1 = Vector128.Multiply(Vector128.Subtract(node->UpperY, orgY), rdirY);
                    sMin = Vector128.Max(sMin, posY ? s0 : s1);
                    sMax = Vector128.Min(sMax, posY ? s1 : s0);
                    Vector128<float> nodeSeg = Vector128.LessThanOrEqual(Vector128.Max(sMin, zero), Vector128.Min(sMax, maxTVec));
                    int nodeMask = (int)Vector128.BitwiseAnd(nodeSeg, Vector128.LessThanOrEqual(sMin, Vector128.Create(bestT))).ExtractMostSignificantBits();
                    if (nodeMask == 0)
                    {
                        break;
                    }

                    int count = 0;
                    for (int i = 0; i < Width; i++)
                    {
                        if ((nodeMask & (1 << i)) != 0)
                        {
                            int child = node->Children[i];
                            float dist = sMin.GetElement(i);
                            int j = count++;
                            while (j > 0 && orderDist[j - 1] > dist)
                            {
                                orderDist[j] = orderDist[j - 1];
                                orderChild[j] = orderChild[j - 1];
                                j--;
                            }
                            orderDist[j] = dist;
                            orderChild[j] = child;
                        }
                    }

                    cur = orderChild[0];
                    curDist = orderDist[0];
                    for (int k = count - 1; k >= 1; k--)
                    {
                        stack[sp].Child = orderChild[k];
                        stack[sp].Dist = orderDist[k];
                        sp++;
                    }
                }

                if (sp == 0)
                {
                    break;
                }
                sp--;
                cur = stack[sp].Child;
                curDist = stack[sp].Dist;
            }

            if (hasHit)
            {
                hitIndex = bestIndex;
                hitT = bestT;
                return true;
            }
            return false;
        }

        private void VisitRayClosestCore<TVisitor>(Vector2 origin, Vector2 displacement, float segmentMaxT, float initialBestT, StackEntry* stack, ref TVisitor visitor)
            where TVisitor : IBvhAabb2DRayClosestVisitor, allows ref struct
        {
            float invX = displacement.X != 0f ? 1f / displacement.X : float.MaxValue;
            float invY = displacement.Y != 0f ? 1f / displacement.Y : float.MaxValue;
            bool posX = invX >= 0f;
            bool posY = invY >= 0f;
            Vector128<float> orgX = Vector128.Create(origin.X);
            Vector128<float> orgY = Vector128.Create(origin.Y);
            Vector128<float> rdirX = Vector128.Create(invX);
            Vector128<float> rdirY = Vector128.Create(invY);
            Vector128<float> zero = Vector128<float>.Zero;
            Vector128<float> maxTVec = Vector128.Create(segmentMaxT);

            Node* nodes = _nodes;
            Leaf* leaves = _leaves;

            float bestT = initialBestT;
            int* orderChild = stackalloc int[Width];
            float* orderDist = stackalloc float[Width];
            int sp = 0;

            int cur = _rootRef;
            float curDist = 0f;

            while (true)
            {
                while (true)
                {
                    if (curDist > bestT)
                    {
                        break;
                    }

                    if (cur < 0)
                    {
                        Leaf* leaf = leaves + ~cur;
                        Vector128<float> t0 = Vector128.Multiply(Vector128.Subtract(leaf->LowerX, orgX), rdirX);
                        Vector128<float> t1 = Vector128.Multiply(Vector128.Subtract(leaf->UpperX, orgX), rdirX);
                        Vector128<float> tMin = posX ? t0 : t1;
                        Vector128<float> tMax = posX ? t1 : t0;
                        t0 = Vector128.Multiply(Vector128.Subtract(leaf->LowerY, orgY), rdirY);
                        t1 = Vector128.Multiply(Vector128.Subtract(leaf->UpperY, orgY), rdirY);
                        tMin = Vector128.Max(tMin, posY ? t0 : t1);
                        tMax = Vector128.Min(tMax, posY ? t1 : t0);
                        Vector128<float> leafSeg = Vector128.LessThanOrEqual(Vector128.Max(tMin, zero), Vector128.Min(tMax, maxTVec));
                        int leafMask = (int)Vector128.BitwiseAnd(leafSeg, Vector128.LessThanOrEqual(tMin, Vector128.Create(bestT))).ExtractMostSignificantBits();
                        if (leafMask != 0)
                        {
                            for (int i = 0; i < Width; i++)
                            {
                                if ((leafMask & (1 << i)) != 0)
                                {
                                    int payload = leaf->Indices[i];
                                    if (payload != EmptyLeafPayload)
                                    {
                                        float entryT = tMin.GetElement(i);
                                        if (entryT <= bestT)
                                        {
                                            float nextBest = visitor.OnLeaf(payload, entryT, bestT);
                                            if (nextBest < bestT)
                                            {
                                                bestT = nextBest;
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        break;
                    }

                    Node* node = nodes + cur;
                    Vector128<float> s0 = Vector128.Multiply(Vector128.Subtract(node->LowerX, orgX), rdirX);
                    Vector128<float> s1 = Vector128.Multiply(Vector128.Subtract(node->UpperX, orgX), rdirX);
                    Vector128<float> sMin = posX ? s0 : s1;
                    Vector128<float> sMax = posX ? s1 : s0;
                    s0 = Vector128.Multiply(Vector128.Subtract(node->LowerY, orgY), rdirY);
                    s1 = Vector128.Multiply(Vector128.Subtract(node->UpperY, orgY), rdirY);
                    sMin = Vector128.Max(sMin, posY ? s0 : s1);
                    sMax = Vector128.Min(sMax, posY ? s1 : s0);
                    Vector128<float> nodeSeg = Vector128.LessThanOrEqual(Vector128.Max(sMin, zero), Vector128.Min(sMax, maxTVec));
                    int nodeMask = (int)Vector128.BitwiseAnd(nodeSeg, Vector128.LessThanOrEqual(sMin, Vector128.Create(bestT))).ExtractMostSignificantBits();
                    if (nodeMask == 0)
                    {
                        break;
                    }

                    int count = 0;
                    for (int i = 0; i < Width; i++)
                    {
                        if ((nodeMask & (1 << i)) != 0)
                        {
                            int child = node->Children[i];
                            float dist = sMin.GetElement(i);
                            int j = count++;
                            while (j > 0 && orderDist[j - 1] > dist)
                            {
                                orderDist[j] = orderDist[j - 1];
                                orderChild[j] = orderChild[j - 1];
                                j--;
                            }
                            orderDist[j] = dist;
                            orderChild[j] = child;
                        }
                    }

                    cur = orderChild[0];
                    curDist = orderDist[0];
                    for (int k = count - 1; k >= 1; k--)
                    {
                        stack[sp].Child = orderChild[k];
                        stack[sp].Dist = orderDist[k];
                        sp++;
                    }
                }

                if (sp == 0)
                {
                    break;
                }
                sp--;
                cur = stack[sp].Child;
                curDist = stack[sp].Dist;
            }
        }

        private bool RayCastAnyCore(Vector2 origin, Vector2 displacement, float maxT, int* stack)
        {
            float invX = displacement.X != 0f ? 1f / displacement.X : float.MaxValue;
            float invY = displacement.Y != 0f ? 1f / displacement.Y : float.MaxValue;
            bool posX = invX >= 0f;
            bool posY = invY >= 0f;
            Vector128<float> orgX = Vector128.Create(origin.X);
            Vector128<float> orgY = Vector128.Create(origin.Y);
            Vector128<float> rdirX = Vector128.Create(invX);
            Vector128<float> rdirY = Vector128.Create(invY);
            Vector128<float> zero = Vector128<float>.Zero;
            Vector128<float> maxTVec = Vector128.Create(maxT);

            Node* nodes = _nodes;
            Leaf* leaves = _leaves;

            int sp = 0;
            int cur = _rootRef;

            while (true)
            {
                while (true)
                {
                    if (cur < 0)
                    {
                        Leaf* leaf = leaves + ~cur;
                        Vector128<float> t0 = Vector128.Multiply(Vector128.Subtract(leaf->LowerX, orgX), rdirX);
                        Vector128<float> t1 = Vector128.Multiply(Vector128.Subtract(leaf->UpperX, orgX), rdirX);
                        Vector128<float> tMin = posX ? t0 : t1;
                        Vector128<float> tMax = posX ? t1 : t0;
                        t0 = Vector128.Multiply(Vector128.Subtract(leaf->LowerY, orgY), rdirY);
                        t1 = Vector128.Multiply(Vector128.Subtract(leaf->UpperY, orgY), rdirY);
                        tMin = Vector128.Max(tMin, posY ? t0 : t1);
                        tMax = Vector128.Min(tMax, posY ? t1 : t0);
                        int leafMask = (int)Vector128.LessThanOrEqual(Vector128.Max(tMin, zero), Vector128.Min(tMax, maxTVec)).ExtractMostSignificantBits();
                        for (int i = 0; i < Width; i++)
                        {
                            if ((leafMask & (1 << i)) != 0 && leaf->Indices[i] != EmptyLeafPayload)
                            {
                                return true;
                            }
                        }
                        break;
                    }

                    Node* node = nodes + cur;
                    Vector128<float> s0 = Vector128.Multiply(Vector128.Subtract(node->LowerX, orgX), rdirX);
                    Vector128<float> s1 = Vector128.Multiply(Vector128.Subtract(node->UpperX, orgX), rdirX);
                    Vector128<float> sMin = posX ? s0 : s1;
                    Vector128<float> sMax = posX ? s1 : s0;
                    s0 = Vector128.Multiply(Vector128.Subtract(node->LowerY, orgY), rdirY);
                    s1 = Vector128.Multiply(Vector128.Subtract(node->UpperY, orgY), rdirY);
                    sMin = Vector128.Max(sMin, posY ? s0 : s1);
                    sMax = Vector128.Min(sMax, posY ? s1 : s0);
                    int nodeMask = (int)Vector128.LessThanOrEqual(Vector128.Max(sMin, zero), Vector128.Min(sMax, maxTVec)).ExtractMostSignificantBits();
                    if (nodeMask == 0)
                    {
                        break;
                    }

                    for (int i = Width - 1; i >= 0; i--)
                    {
                        if ((nodeMask & (1 << i)) != 0)
                        {
                            stack[sp++] = node->Children[i];
                        }
                    }
                    break;
                }

                if (sp == 0)
                {
                    break;
                }
                cur = stack[--sp];
            }
            return false;
        }

        private void VisitRayAllCore<TVisitor>(Vector2 origin, Vector2 displacement, float maxT, int* stack, ref TVisitor visitor)
            where TVisitor : IBvhAabb2DLeafVisitor, allows ref struct
        {
            float invX = displacement.X != 0f ? 1f / displacement.X : float.MaxValue;
            float invY = displacement.Y != 0f ? 1f / displacement.Y : float.MaxValue;
            bool posX = invX >= 0f;
            bool posY = invY >= 0f;
            Vector128<float> orgX = Vector128.Create(origin.X);
            Vector128<float> orgY = Vector128.Create(origin.Y);
            Vector128<float> rdirX = Vector128.Create(invX);
            Vector128<float> rdirY = Vector128.Create(invY);
            Vector128<float> zero = Vector128<float>.Zero;
            Vector128<float> maxTVec = Vector128.Create(maxT);

            Node* nodes = _nodes;
            Leaf* leaves = _leaves;

            int sp = 0;
            int cur = _rootRef;

            while (true)
            {
                while (true)
                {
                    if (cur < 0)
                    {
                        Leaf* leaf = leaves + ~cur;
                        Vector128<float> t0 = Vector128.Multiply(Vector128.Subtract(leaf->LowerX, orgX), rdirX);
                        Vector128<float> t1 = Vector128.Multiply(Vector128.Subtract(leaf->UpperX, orgX), rdirX);
                        Vector128<float> tMin = posX ? t0 : t1;
                        Vector128<float> tMax = posX ? t1 : t0;
                        t0 = Vector128.Multiply(Vector128.Subtract(leaf->LowerY, orgY), rdirY);
                        t1 = Vector128.Multiply(Vector128.Subtract(leaf->UpperY, orgY), rdirY);
                        tMin = Vector128.Max(tMin, posY ? t0 : t1);
                        tMax = Vector128.Min(tMax, posY ? t1 : t0);
                        int leafMask = (int)Vector128.LessThanOrEqual(Vector128.Max(tMin, zero), Vector128.Min(tMax, maxTVec)).ExtractMostSignificantBits();
                        for (int i = 0; i < Width; i++)
                        {
                            if ((leafMask & (1 << i)) != 0)
                            {
                                int payload = leaf->Indices[i];
                                if (payload != EmptyLeafPayload && !visitor.OnLeaf(payload))
                                {
                                    return;
                                }
                            }
                        }
                        break;
                    }

                    Node* node = nodes + cur;
                    Vector128<float> s0 = Vector128.Multiply(Vector128.Subtract(node->LowerX, orgX), rdirX);
                    Vector128<float> s1 = Vector128.Multiply(Vector128.Subtract(node->UpperX, orgX), rdirX);
                    Vector128<float> sMin = posX ? s0 : s1;
                    Vector128<float> sMax = posX ? s1 : s0;
                    s0 = Vector128.Multiply(Vector128.Subtract(node->LowerY, orgY), rdirY);
                    s1 = Vector128.Multiply(Vector128.Subtract(node->UpperY, orgY), rdirY);
                    sMin = Vector128.Max(sMin, posY ? s0 : s1);
                    sMax = Vector128.Min(sMax, posY ? s1 : s0);
                    int nodeMask = (int)Vector128.LessThanOrEqual(Vector128.Max(sMin, zero), Vector128.Min(sMax, maxTVec)).ExtractMostSignificantBits();
                    if (nodeMask == 0)
                    {
                        break;
                    }

                    for (int i = Width - 1; i >= 0; i--)
                    {
                        if ((nodeMask & (1 << i)) != 0)
                        {
                            stack[sp++] = node->Children[i];
                        }
                    }
                    break;
                }

                if (sp == 0)
                {
                    break;
                }
                cur = stack[--sp];
            }
        }

        private void VisitOverlapAabbCore<TVisitor>(in BoundingBox2D query, int* stack, ref TVisitor visitor)
            where TVisitor : IBvhAabb2DLeafVisitor, allows ref struct
        {
            Vector128<float> qMinX = Vector128.Create(query.Min.X);
            Vector128<float> qMinY = Vector128.Create(query.Min.Y);
            Vector128<float> qMaxX = Vector128.Create(query.Max.X);
            Vector128<float> qMaxY = Vector128.Create(query.Max.Y);

            Node* nodes = _nodes;
            Leaf* leaves = _leaves;

            int sp = 0;
            int cur = _rootRef;

            while (true)
            {
                while (true)
                {
                    if (cur < 0)
                    {
                        Leaf* leaf = leaves + ~cur;
                        Vector128<float> leafValid = Vector128.BitwiseAnd(
                            Vector128.GreaterThanOrEqual(leaf->UpperX, qMinX),
                            Vector128.LessThanOrEqual(leaf->LowerX, qMaxX));
                        leafValid = Vector128.BitwiseAnd(leafValid, Vector128.BitwiseAnd(
                            Vector128.GreaterThanOrEqual(leaf->UpperY, qMinY),
                            Vector128.LessThanOrEqual(leaf->LowerY, qMaxY)));
                        int leafMask = (int)leafValid.ExtractMostSignificantBits();
                        for (int i = 0; i < Width; i++)
                        {
                            if ((leafMask & (1 << i)) != 0)
                            {
                                int payload = leaf->Indices[i];
                                if (payload != EmptyLeafPayload && !visitor.OnLeaf(payload))
                                {
                                    return;
                                }
                            }
                        }
                        break;
                    }

                    Node* node = nodes + cur;
                    Vector128<float> nodeValid = Vector128.BitwiseAnd(
                        Vector128.GreaterThanOrEqual(node->UpperX, qMinX),
                        Vector128.LessThanOrEqual(node->LowerX, qMaxX));
                    nodeValid = Vector128.BitwiseAnd(nodeValid, Vector128.BitwiseAnd(
                        Vector128.GreaterThanOrEqual(node->UpperY, qMinY),
                        Vector128.LessThanOrEqual(node->LowerY, qMaxY)));
                    int nodeMask = (int)nodeValid.ExtractMostSignificantBits();
                    if (nodeMask == 0)
                    {
                        break;
                    }

                    for (int i = Width - 1; i >= 0; i--)
                    {
                        if ((nodeMask & (1 << i)) != 0)
                        {
                            stack[sp++] = node->Children[i];
                        }
                    }
                    break;
                }

                if (sp == 0)
                {
                    break;
                }
                cur = stack[--sp];
            }
        }

        private void VisitPointCore<TVisitor>(Vector2 point, int* stack, ref TVisitor visitor)
            where TVisitor : IBvhAabb2DLeafVisitor, allows ref struct
        {
            Vector128<float> px = Vector128.Create(point.X);
            Vector128<float> py = Vector128.Create(point.Y);

            Node* nodes = _nodes;
            Leaf* leaves = _leaves;

            int sp = 0;
            int cur = _rootRef;

            while (true)
            {
                while (true)
                {
                    if (cur < 0)
                    {
                        Leaf* leaf = leaves + ~cur;
                        Vector128<float> leafValid = Vector128.BitwiseAnd(
                            Vector128.GreaterThanOrEqual(leaf->UpperX, px),
                            Vector128.LessThanOrEqual(leaf->LowerX, px));
                        leafValid = Vector128.BitwiseAnd(leafValid, Vector128.BitwiseAnd(
                            Vector128.GreaterThanOrEqual(leaf->UpperY, py),
                            Vector128.LessThanOrEqual(leaf->LowerY, py)));
                        int leafMask = (int)leafValid.ExtractMostSignificantBits();
                        for (int i = 0; i < Width; i++)
                        {
                            if ((leafMask & (1 << i)) != 0)
                            {
                                int payload = leaf->Indices[i];
                                if (payload != EmptyLeafPayload && !visitor.OnLeaf(payload))
                                {
                                    return;
                                }
                            }
                        }
                        break;
                    }

                    Node* node = nodes + cur;
                    Vector128<float> nodeValid = Vector128.BitwiseAnd(
                        Vector128.GreaterThanOrEqual(node->UpperX, px),
                        Vector128.LessThanOrEqual(node->LowerX, px));
                    nodeValid = Vector128.BitwiseAnd(nodeValid, Vector128.BitwiseAnd(
                        Vector128.GreaterThanOrEqual(node->UpperY, py),
                        Vector128.LessThanOrEqual(node->LowerY, py)));
                    int nodeMask = (int)nodeValid.ExtractMostSignificantBits();
                    if (nodeMask == 0)
                    {
                        break;
                    }

                    for (int i = Width - 1; i >= 0; i--)
                    {
                        if ((nodeMask & (1 << i)) != 0)
                        {
                            stack[sp++] = node->Children[i];
                        }
                    }
                    break;
                }

                if (sp == 0)
                {
                    break;
                }
                cur = stack[--sp];
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            Clear();
            _defaultBuilder?.Dispose();
            _defaultBuilder = null;
            _isDisposed = true;
        }
    }
}
