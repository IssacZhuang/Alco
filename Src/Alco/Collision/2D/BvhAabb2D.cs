using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Alco
{
    /// <summary>
    /// A flat, AABB-only bounding volume hierarchy for 2D workloads (frustum culling,
    /// ray picking, visibility queries). Built with the same Morton-code LBVH (Karras 2012)
    /// algorithm as <see cref="NativeBvh2D"/>, but stores only an integer index per
    /// leaf instead of a <see cref="ColliderRef2D"/> pointer, yielding smaller nodes and
    /// zero pointer-chasing during traversal.
    /// <para>
    /// Build and queries are separated: <see cref="Build"/> constructs the tree (not thread-safe),
    /// and all query methods are safe for concurrent reads.
    /// </para>
    /// </summary>
    public unsafe class BvhAabb2D : IDisposable
    {
        private NativeBuffer<BvhAabbNode2D> _nodes;
        private int _rootIndex = -1;
        private int _nodeCount;
        private int _treeDepth;
        private bool _isDisposed;

        // ── Morton builder scratch (reused across builds, zero managed allocs after warm-up) ──

        private NativeBuffer<ulong> _sortPairs; // (mortonCode << 32) | leafIndex, ping-pong for radix sort
        private NativeBuffer<int> _perm;         // slot → source leaf index for in-place gather

        private const int BitsPerAxis = 10;
        private const int RadixBits = 10;
        private const int RadixBuckets = 1 << RadixBits;
        private const int PassCount = (BitsPerAxis * 2 + RadixBits - 1) / RadixBits; // = 2

        /// <summary>Current number of nodes in the tree.</summary>
        public int NodeCount => _nodeCount;

        /// <summary>Current tree depth (max stack depth for traversal).</summary>
        public int TreeDepth => _treeDepth;

        // ════════════════════════════════════════════════════════════
        //  Build
        // ════════════════════════════════════════════════════════════

        /// <summary>
        /// Builds the BVH from a span of bounding boxes using Morton-code LBVH.
        /// Each AABB is assigned index 0..n-1 (its position in the input span).
        /// </summary>
        public void Build(ReadOnlySpan<BoundingBox2D> bounds)
        {
            int n = bounds.Length;
            if (n == 0)
            {
                _rootIndex = -1;
                _nodeCount = 0;
                _treeDepth = 0;
                return;
            }

            // allocate node buffer: n leaves + (n-1) internal + slack
            _nodes.SetSizeWithoutCopy(n * 2 + (int)Math.Sqrt(n) + 2);
            EnsureScratch(n);

            // ── write leaves in input order, track centroid scene bounds ──

            Vector2 sceneMin = new(float.MaxValue);
            Vector2 sceneMax = new(float.MinValue);

            for (int i = 0; i < n; i++)
            {
                BoundingBox2D b = bounds[i];
                _nodes.UnsafePointer[i] = new BvhAabbNode2D
                {
                    Bounds = b,
                    Left = -1,
                    Right = -1,
                    Index = i,
                };

                Vector2 c2 = b.Min + b.Max; // 2× center, avoids division per leaf
                sceneMin = Vector2.Min(sceneMin, c2);
                sceneMax = Vector2.Max(sceneMax, c2);
            }

            if (n == 1)
            {
                _rootIndex = 0;
                _nodeCount = 1;
                _treeDepth = 1;
                return;
            }

            // ── compute 20-bit Morton codes, packed (code << 32) | index ──

            ulong* pairs = _sortPairs.UnsafePointer;
            Vector2 extent = sceneMax - sceneMin;
            float invX = extent.X > 0 ? 1f / extent.X : 0f;
            float invY = extent.Y > 0 ? 1f / extent.Y : 0f;

            for (int i = 0; i < n; i++)
            {
                BoundingBox2D b = _nodes.UnsafePointer[i].Bounds;
                Vector2 c2 = b.Min + b.Max;
                uint code = MortonCode(
                    (c2.X - sceneMin.X) * invX,
                    (c2.Y - sceneMin.Y) * invY);
                pairs[i] = ((ulong)code << 32) | (uint)i;
            }

            // ── LSD radix sort over 20-bit codes (2 passes × 10 bits) ──

            ulong* sorted = RadixSort(pairs, _sortPairs.UnsafePointer + n, n);

            // ── in-place gather: reorder leaves to sorted order ──

            int* perm = _perm.UnsafePointer;
            for (int i = 0; i < n; i++)
                perm[i] = (int)(uint)sorted[i];
            ApplyPermutation(_nodes.AsSpan(), perm, n);

            // ── binary radix tree construction (recursive, bottom-up) ──

            int internalCounter = n;
            int maxDepth = 1;
            _rootIndex = Emit(_nodes.AsSpan(), sorted, 0, n, ref internalCounter, ref maxDepth, 1);

            _nodeCount = internalCounter;
            _treeDepth = maxDepth;
        }

        // ════════════════════════════════════════════════════════════
        //  Queries
        // ════════════════════════════════════════════════════════════

        /// <summary>
        /// Casts a ray segment (origin + displacement·t, t ∈ [0,1]) and returns the closest
        /// leaf whose AABB the ray enters. Internal nodes use cheap AABB-overlap pruning
        /// (matching the collider BVH strategy); leaves use the slab method to obtain the
        /// entry fraction for closest-hit comparison.
        /// </summary>
        /// <returns>True if any leaf was hit; <paramref name="hitT"/> is the entry fraction.</returns>
        public bool RayCastClosest(Vector2 origin, Vector2 displacement, out int hitIndex, out float hitT)
        {
            hitIndex = -1;
            hitT = 0f;

            if (_nodeCount == 0 || _rootIndex < 0)
                return false;

            Vector2 end = origin + displacement;
            BoundingBox2D rayBox = new(Vector2.Min(origin, end), Vector2.Max(origin, end));

            float invX = displacement.X != 0 ? 1f / displacement.X : float.MaxValue;
            float invY = displacement.Y != 0 ? 1f / displacement.Y : float.MaxValue;

            float bestT = 1f; // segments: t ∈ [0, 1]
            int bestIndex = -1;

            int* stack = stackalloc int[_treeDepth];
            int sp = 0;
            stack[sp++] = _rootIndex;

            while (sp > 0)
            {
                BvhAabbNode2D node = _nodes.UnsafePointer[stack[--sp]];

                if (!rayBox.Intersects(node.Bounds))
                    continue;

                if (node.IsLeaf)
                {
                    // Slab test at leaves only: gives entry distance for closest-hit ordering.
                    if (RayIntersectsAabb(origin, invX, invY, bestT, node.Bounds.Min, node.Bounds.Max, out float tMin))
                    {
                        bestT = tMin;
                        bestIndex = node.Index;
                    }
                    continue;
                }

                stack[sp++] = node.Left;
                stack[sp++] = node.Right;
            }

            if (bestIndex >= 0)
            {
                hitIndex = bestIndex;
                hitT = bestT;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Casts a ray segment and returns on the first hit (any-hit query).
        /// Uses AABB-overlap at every node — no slab computation needed.
        /// Faster than <see cref="RayCastClosest"/> for visibility/shadow tests.
        /// </summary>
        public bool RayCastAny(Vector2 origin, Vector2 displacement)
        {
            if (_nodeCount == 0 || _rootIndex < 0)
                return false;

            Vector2 end = origin + displacement;
            BoundingBox2D rayBox = new(Vector2.Min(origin, end), Vector2.Max(origin, end));

            int* stack = stackalloc int[_treeDepth];
            int sp = 0;
            stack[sp++] = _rootIndex;

            while (sp > 0)
            {
                BvhAabbNode2D node = _nodes.UnsafePointer[stack[--sp]];

                if (!rayBox.Intersects(node.Bounds))
                    continue;

                if (node.IsLeaf)
                    return true;

                stack[sp++] = node.Left;
                stack[sp++] = node.Right;
            }
            return false;
        }

        /// <summary>
        /// Finds all leaf AABBs that overlap <paramref name="query"/>.
        /// Results are appended to <paramref name="results"/>.
        /// </summary>
        public void OverlapAabb(in BoundingBox2D query, List<int> results)
        {
            if (_nodeCount == 0 || _rootIndex < 0)
                return;

            int* stack = stackalloc int[_treeDepth];
            int sp = 0;
            stack[sp++] = _rootIndex;

            while (sp > 0)
            {
                BvhAabbNode2D node = _nodes.UnsafePointer[stack[--sp]];

                if (!query.Intersects(node.Bounds))
                    continue;

                if (node.IsLeaf)
                {
                    results.Add(node.Index);
                    continue;
                }

                stack[sp++] = node.Left;
                stack[sp++] = node.Right;
            }
        }

        /// <summary>
        /// Finds all leaf AABBs that contain <paramref name="point"/>.
        /// Results are appended to <paramref name="results"/>.
        /// </summary>
        public void QueryPoint(Vector2 point, List<int> results)
        {
            if (_nodeCount == 0 || _rootIndex < 0)
                return;

            int* stack = stackalloc int[_treeDepth];
            int sp = 0;
            stack[sp++] = _rootIndex;

            while (sp > 0)
            {
                BvhAabbNode2D node = _nodes.UnsafePointer[stack[--sp]];

                if (!node.Bounds.Contains(point))
                    continue;

                if (node.IsLeaf)
                {
                    results.Add(node.Index);
                    continue;
                }

                stack[sp++] = node.Left;
                stack[sp++] = node.Right;
            }
        }

        // ════════════════════════════════════════════════════════════
        //  Morton LBVH internals (adapted from MortonBvhBuilder2D)
        // ════════════════════════════════════════════════════════════

        private void EnsureScratch(int leafCount)
        {
            if (_sortPairs.Capacity < leafCount * 2)
            {
                _sortPairs.Dispose();
                _sortPairs = new NativeBuffer<ulong>(leafCount * 2);
            }
            if (_perm.Capacity < leafCount)
            {
                _perm.Dispose();
                _perm = new NativeBuffer<int>(leafCount);
            }
        }

        /// <summary>Recursively split the sorted code range at the highest differing bit, emit internal nodes bottom-up.</summary>
        private static int Emit(Span<BvhAabbNode2D> nodes, ulong* sorted, int start, int end,
                                ref int internalCounter, ref int maxDepth, int depth)
        {
            if (end - start == 1)
            {
                if (depth > maxDepth) maxDepth = depth;
                return start; // leaf slot
            }

            uint first = (uint)(sorted[start] >> 32);
            uint last = (uint)(sorted[end - 1] >> 32);
            uint xor = first ^ last;

            int split;
            if (xor == 0)
            {
                split = (start + end) / 2; // identical codes: midpoint fallback
            }
            else
            {
                uint splitBit = 0x80000000u >> BitOperations.LeadingZeroCount(xor);
                int lo = start + 1;
                int hi = end - 1;
                while (lo < hi)
                {
                    int mid = (lo + hi) / 2;
                    if (((uint)(sorted[mid] >> 32) & splitBit) == 0)
                        lo = mid + 1;
                    else
                        hi = mid;
                }
                split = lo;
            }

            int left = Emit(nodes, sorted, start, split, ref internalCounter, ref maxDepth, depth + 1);
            int right = Emit(nodes, sorted, split, end, ref internalCounter, ref maxDepth, depth + 1);

            int index = internalCounter++;
            nodes[index] = new BvhAabbNode2D
            {
                Left = left,
                Right = right,
                Bounds = BoundingBox2D.Merge(nodes[left].Bounds, nodes[right].Bounds),
                Index = -1,
            };
            return index;
        }

        private static ulong* RadixSort(ulong* src, ulong* dst, int n)
        {
            int* counts = stackalloc int[RadixBuckets];

            for (int pass = 0; pass < PassCount; pass++)
            {
                int shift = 32 + pass * RadixBits;

                for (int i = 0; i < RadixBuckets; i++)
                    counts[i] = 0;
                for (int i = 0; i < n; i++)
                    counts[(int)((src[i] >> shift) & (RadixBuckets - 1))]++;

                int sum = 0;
                for (int i = 0; i < RadixBuckets; i++)
                {
                    int c = counts[i];
                    counts[i] = sum;
                    sum += c;
                }

                for (int i = 0; i < n; i++)
                {
                    ulong pair = src[i];
                    dst[counts[(int)((pair >> shift) & (RadixBuckets - 1))]++] = pair;
                }

                ulong* tmp = src;
                src = dst;
                dst = tmp;
            }
            return src;
        }

        private static void ApplyPermutation(Span<BvhAabbNode2D> leaves, int* perm, int n)
        {
            for (int i = 0; i < n; i++)
            {
                if (perm[i] < 0) continue;

                BvhAabbNode2D saved = leaves[i];
                int j = i;
                while (true)
                {
                    int src = perm[j];
                    perm[j] = ~src;
                    if (src == i)
                    {
                        leaves[j] = saved;
                        break;
                    }
                    leaves[j] = leaves[src];
                    j = src;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint MortonCode(float x, float y)
        {
            const float scale = (1 << BitsPerAxis) - 1;
            uint xi = (uint)Math.Min(Math.Max(x * scale, 0f), scale);
            uint yi = (uint)Math.Min(Math.Max(y * scale, 0f), scale);
            return (Part1By1(xi) << 1) | Part1By1(yi);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint Part1By1(uint v)
        {
            v = (v | (v << 8)) & 0x00FF00FFu;
            v = (v | (v << 4)) & 0x0F0F0F0Fu;
            v = (v | (v << 2)) & 0x33333333u;
            v = (v | (v << 1)) & 0x55555555u;
            return v;
        }

        /// <summary>2D slab-method ray-AABB test. invDir components must handle parallel axes (use MaxValue for zero).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool RayIntersectsAabb(
            Vector2 origin, float invX, float invY, float maxT,
            Vector2 bmin, Vector2 bmax,
            out float tMin)
        {
            float tx1 = (bmin.X - origin.X) * invX;
            float tx2 = (bmax.X - origin.X) * invX;
            float tmin = Math.Min(tx1, tx2);
            float tmax = Math.Max(tx1, tx2);

            float ty1 = (bmin.Y - origin.Y) * invY;
            float ty2 = (bmax.Y - origin.Y) * invY;
            tmin = Math.Max(tmin, Math.Min(ty1, ty2));
            tmax = Math.Min(tmax, Math.Max(ty1, ty2));

            tMin = tmin;
            return tmax >= Math.Max(tmin, 0f) && tmin <= maxT;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (_isDisposed) return;
            _nodes.Dispose();
            _sortPairs.Dispose();
            _perm.Dispose();
            _isDisposed = true;
        }
    }
}
