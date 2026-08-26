using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Alco
{
    /// <summary>
    /// A flat, 4-wide AABB bounding volume hierarchy for 2D workloads (frustum culling,
    /// ray picking, visibility queries), built with the same Morton-code LBVH (Karras 2012)
    /// pipeline as <see cref="NativeBvh2D"/>.
    /// <para>
    /// - Nodes store the SoA bounds of their (up to) 4 children in an 80-byte block, so one
    ///   Vector128 slab test covers all children with no padding.
    /// - Child references are tagged in the sign bit (&gt;= 0 node index, &lt; 0 leaf block);
    ///   unused leaf slots carry empty bounds that fail every intersection test.
    /// - The 4-way split is a "falling split" over the binary Morton splits; children are
    ///   ordered by descending subtree size for any-hit queries.
    /// </para>
    /// <see cref="Build"/> is not thread-safe; all query methods are safe for concurrent reads.
    /// </summary>
    public unsafe class BvhAabb2D : IDisposable
    {
        /// <summary>Fanout of every internal node.</summary>
        public const int Width = 4;

        /// <summary>Maximum number of items stored in one leaf block.</summary>
        public const int MaxLeafItems = 4;

        private const int MaxBuildDepth = 64;
        private const int EmptyRef = int.MinValue;

        /// <summary>
        /// SoA bounds of the (up to) 4 children plus tagged child references, 80 bytes
        /// (4×16 bounds + 4×4 references); no padding: everything is touched by traversal.
        /// </summary>
        [StructLayout(LayoutKind.Sequential, Size = 80)]
        public struct Node
        {
            /// <summary>Child lower bounds, X lane per slot.</summary>
            public Vector128<float> LowerX;
            /// <summary>Child upper bounds, X lane per slot.</summary>
            public Vector128<float> UpperX;
            /// <summary>Child lower bounds, Y lane per slot.</summary>
            public Vector128<float> LowerY;
            /// <summary>Child upper bounds, Y lane per slot.</summary>
            public Vector128<float> UpperY;

            /// <summary>Child references: &gt;= 0 node index, &lt; 0 leaf block (~v), <see cref="EmptyRef"/> unused slot.</summary>
            public fixed int Children[4];
        }

        /// <summary>
        /// Leaf block of up to 4 items packed SoA; unused slots carry empty bounds
        /// (lower = +inf, upper = -inf) that fail every test.
        /// </summary>
        [StructLayout(LayoutKind.Sequential, Size = 80)]
        public struct Leaf
        {
            /// <summary>Item lower bounds, X lane per slot.</summary>
            public Vector128<float> LowerX;
            /// <summary>Item upper bounds, X lane per slot.</summary>
            public Vector128<float> UpperX;
            /// <summary>Item lower bounds, Y lane per slot.</summary>
            public Vector128<float> LowerY;
            /// <summary>Item upper bounds, Y lane per slot.</summary>
            public Vector128<float> UpperY;

            /// <summary>User item indices, -1 for unused slots.</summary>
            public fixed int Indices[4];
        }

        private struct Item
        {
            public BoundingBox2D Bounds;
            public int Index;
        }

        private struct StackEntry
        {
            public int Child;
            public float Dist;
        }

        private NativeBuffer<Node> _nodes;
        private NativeBuffer<Leaf> _leaves;
        private NativeBuffer<ulong> _pairs;
        private NativeBuffer<Item> _items;
        private ulong* _sorted; // radix sort output, valid during Build only
        private int _rootRef = EmptyRef;
        private int _nodeCount;
        private int _leafCount;
        private int _treeDepth;
        private bool _isDisposed;

        private const int BitsPerAxis = 10;
        private const int RadixBits = 10;
        private const int RadixBuckets = 1 << RadixBits;
        private const int PassCount = (BitsPerAxis * 2 + RadixBits - 1) / RadixBits; // = 2

        private static readonly BoundingBox2D EmptyBox = new(new Vector2(float.MaxValue), new Vector2(float.MinValue));

        /// <summary>Current number of internal nodes in the tree.</summary>
        public int NodeCount => _nodeCount;

        /// <summary>Current tree depth (max stack depth for traversal).</summary>
        public int TreeDepth => _treeDepth;

        // ════════════════════════════════════════════════════════════
        //  Build
        // ════════════════════════════════════════════════════════════

        /// <summary>
        /// Builds the tree from a span of bounding boxes using the Morton LBVH pipeline
        /// (20-bit codes, LSD radix sort, highest-differing-bit splits), emitting 4-wide
        /// nodes and leaf blocks. Each AABB is referenced by its position in the input span.
        /// </summary>
        public void Build(ReadOnlySpan<BoundingBox2D> bounds)
        {
            int n = bounds.Length;
            if (n == 0)
            {
                _rootRef = EmptyRef;
                _nodeCount = 0;
                _leafCount = 0;
                _treeDepth = 0;
                return;
            }

            EnsureScratch(n);
            // leaves hold >= 1 item each (<= n blocks), internal nodes have >= 2 child
            // subtrees (<= n - 1 blocks); +16 covers rounding and the single-item tree
            _nodes.SetSizeWithoutCopy(n + 16);
            _leaves.SetSizeWithoutCopy(n + 16);

            // scene bounds of the 2x centroids (avoids a division per item)
            Vector2 sceneMin = new(float.MaxValue);
            Vector2 sceneMax = new(float.MinValue);
            for (int i = 0; i < n; i++)
            {
                Vector2 c2 = bounds[i].Min + bounds[i].Max;
                sceneMin = Vector2.Min(sceneMin, c2);
                sceneMax = Vector2.Max(sceneMax, c2);
            }

            // 20-bit Morton codes packed as (code << 32) | index
            ulong* pairs = _pairs.UnsafePointer;
            Vector2 extent = sceneMax - sceneMin;
            float invX = extent.X > 0 ? 1f / extent.X : 0f;
            float invY = extent.Y > 0 ? 1f / extent.Y : 0f;
            for (int i = 0; i < n; i++)
            {
                Vector2 c2 = bounds[i].Min + bounds[i].Max;
                uint code = MortonCode(
                    (c2.X - sceneMin.X) * invX,
                    (c2.Y - sceneMin.Y) * invY);
                pairs[i] = ((ulong)code << 32) | (uint)i;
            }

            _sorted = RadixSort(pairs, pairs + n, n);

            // gather the items in sorted order so every range of the tree is contiguous
            Item* items = _items.UnsafePointer;
            for (int i = 0; i < n; i++)
            {
                int index = (int)(uint)_sorted[i];
                items[i] = new Item { Bounds = bounds[index], Index = index };
            }

            _nodeCount = 0;
            _leafCount = 0;
            _treeDepth = 0;
            _rootRef = BuildRange(0, n, 1, out _);
            _sorted = null;
        }

        private void EnsureScratch(int leafCount)
        {
            if (_pairs.Capacity < leafCount * 2)
            {
                _pairs.Dispose();
                _pairs = new NativeBuffer<ulong>(leafCount * 2);
            }
            if (_items.Capacity < leafCount)
            {
                _items.Dispose();
                _items = new NativeBuffer<Item>(leafCount);
            }
        }

        /// <summary>
        /// Recursively builds a subtree over the sorted item range [start, end); returns the
        /// tagged root reference and the subtree bounds through <paramref name="bounds"/>.
        /// </summary>
        private int BuildRange(int start, int end, int depth, out BoundingBox2D bounds)
        {
            if (end - start <= MaxLeafItems)
            {
                return EmitLeaf(start, end, depth, out bounds);
            }

            // falling split (Embree bvh_builder_morton.h): repeatedly split the largest
            // subrange with the binary Morton split until the node has up to Width children
            Span<int> rStart = stackalloc int[Width];
            Span<int> rEnd = stackalloc int[Width];
            rStart[0] = start;
            rEnd[0] = end;
            int rangeCount = 1;

            while (rangeCount < Width)
            {
                int best = -1;
                int bestSize = MaxLeafItems;
                for (int i = 0; i < rangeCount; i++)
                {
                    int size = rEnd[i] - rStart[i];
                    if (size > bestSize)
                    {
                        bestSize = size;
                        best = i;
                    }
                }
                if (best < 0)
                {
                    break; // every subrange already fits into a leaf block
                }

                int s = rStart[best];
                int e = rEnd[best];
                int split = depth < MaxBuildDepth ? FindSplit(s, e) : (s + e) / 2;
                rEnd[best] = split;
                rStart[rangeCount] = split;
                rEnd[rangeCount] = e;
                rangeCount++;
            }

            // children ordered by descending item count: any-hit rays expect the bigger
            // subtree first (Embree sorts build records "for faster shadow ray traversal")
            for (int i = 1; i < rangeCount; i++)
            {
                int s = rStart[i];
                int e = rEnd[i];
                int j = i - 1;
                while (j >= 0 && rEnd[j] - rStart[j] < e - s)
                {
                    rStart[j + 1] = rStart[j];
                    rEnd[j + 1] = rEnd[j];
                    j--;
                }
                rStart[j + 1] = s;
                rEnd[j + 1] = e;
            }

            // recurse first so subtrees are laid out contiguously before their parent
            BoundingBox2D b0 = EmptyBox, b1 = EmptyBox, b2 = EmptyBox, b3 = EmptyBox;
            int c0 = BuildRange(rStart[0], rEnd[0], depth + 1, out b0);
            int c1 = BuildRange(rStart[1], rEnd[1], depth + 1, out b1);
            int c2 = rangeCount > 2 ? BuildRange(rStart[2], rEnd[2], depth + 1, out b2) : EmptyRef;
            int c3 = rangeCount > 3 ? BuildRange(rStart[3], rEnd[3], depth + 1, out b3) : EmptyRef;

            int index = _nodeCount++;
            Node* node = _nodes.UnsafePointer + index;
            node->LowerX = Vector128.Create(b0.Min.X, b1.Min.X, b2.Min.X, b3.Min.X);
            node->UpperX = Vector128.Create(b0.Max.X, b1.Max.X, b2.Max.X, b3.Max.X);
            node->LowerY = Vector128.Create(b0.Min.Y, b1.Min.Y, b2.Min.Y, b3.Min.Y);
            node->UpperY = Vector128.Create(b0.Max.Y, b1.Max.Y, b2.Max.Y, b3.Max.Y);
            node->Children[0] = c0;
            node->Children[1] = c1;
            node->Children[2] = c2;
            node->Children[3] = c3;

            bounds = BoundingBox2D.Merge(BoundingBox2D.Merge(b0, b1), BoundingBox2D.Merge(b2, b3));
            return index;
        }

        private int EmitLeaf(int start, int end, int depth, out BoundingBox2D bounds)
        {
            Item* items = _items.UnsafePointer;
            BoundingBox2D b0 = items[start].Bounds;
            BoundingBox2D b1 = end - start > 1 ? items[start + 1].Bounds : EmptyBox;
            BoundingBox2D b2 = end - start > 2 ? items[start + 2].Bounds : EmptyBox;
            BoundingBox2D b3 = end - start > 3 ? items[start + 3].Bounds : EmptyBox;

            int leafIndex = _leafCount++;
            Leaf* leaf = _leaves.UnsafePointer + leafIndex;
            leaf->LowerX = Vector128.Create(b0.Min.X, b1.Min.X, b2.Min.X, b3.Min.X);
            leaf->UpperX = Vector128.Create(b0.Max.X, b1.Max.X, b2.Max.X, b3.Max.X);
            leaf->LowerY = Vector128.Create(b0.Min.Y, b1.Min.Y, b2.Min.Y, b3.Min.Y);
            leaf->UpperY = Vector128.Create(b0.Max.Y, b1.Max.Y, b2.Max.Y, b3.Max.Y);
            leaf->Indices[0] = items[start].Index;
            leaf->Indices[1] = end - start > 1 ? items[start + 1].Index : -1;
            leaf->Indices[2] = end - start > 2 ? items[start + 2].Index : -1;
            leaf->Indices[3] = end - start > 3 ? items[start + 3].Index : -1;

            if (depth > _treeDepth)
            {
                _treeDepth = depth;
            }

            bounds = BoundingBox2D.Merge(BoundingBox2D.Merge(b0, b1), BoundingBox2D.Merge(b2, b3));
            return ~leafIndex;
        }

        /// <summary>Splits the sorted code range at the highest differing bit.</summary>
        private int FindSplit(int start, int end)
        {
            uint first = (uint)(_sorted[start] >> 32);
            uint last = (uint)(_sorted[end - 1] >> 32);
            uint xor = first ^ last;
            if (xor == 0)
            {
                return (start + end) / 2; // identical codes: midpoint fallback
            }

            uint splitBit = 0x80000000u >> BitOperations.LeadingZeroCount(xor);
            int lo = start + 1;
            int hi = end - 1;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (((uint)(_sorted[mid] >> 32) & splitBit) == 0)
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

        // LSD radix sort over the 20-bit codes
        private static ulong* RadixSort(ulong* src, ulong* dst, int n)
        {
            int* counts = stackalloc int[RadixBuckets];

            for (int pass = 0; pass < PassCount; pass++)
            {
                int shift = 32 + pass * RadixBits;

                for (int i = 0; i < RadixBuckets; i++)
                {
                    counts[i] = 0;
                }
                for (int i = 0; i < n; i++)
                {
                    counts[(int)((src[i] >> shift) & (RadixBuckets - 1))]++;
                }

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

        // ════════════════════════════════════════════════════════════
        //  Queries (thread-safe for concurrent reads after Build)
        // ════════════════════════════════════════════════════════════

        /// <summary>
        /// Casts a ray segment (origin + displacement·t, t ∈ [0,1]) and returns the closest
        /// leaf item whose AABB the ray enters; <paramref name="hitT"/> is the raw slab entry,
        /// which can be negative when the origin starts inside a box. All 4 child bounds are
        /// tested with one SIMD slab test; children are traversed near-first and stack entries
        /// carry entry distances so subtrees behind the running best hit are skipped.
        /// </summary>
        /// <returns>True if any leaf was hit; <paramref name="hitT"/> is the entry fraction.</returns>
        public bool RayCastClosest(Vector2 origin, Vector2 displacement, out int hitIndex, out float hitT)
        {
            hitIndex = -1;
            hitT = 0f;
            if (_rootRef == EmptyRef)
            {
                return false;
            }

            float invX = displacement.X != 0f ? 1f / displacement.X : float.MaxValue;
            float invY = displacement.Y != 0f ? 1f / displacement.Y : float.MaxValue;
            bool posX = invX >= 0f;
            bool posY = invY >= 0f;
            Vector128<float> orgX = Vector128.Create(origin.X);
            Vector128<float> orgY = Vector128.Create(origin.Y);
            Vector128<float> rdirX = Vector128.Create(invX);
            Vector128<float> rdirY = Vector128.Create(invY);
            Vector128<float> zero = Vector128<float>.Zero;
            Vector128<float> one = Vector128.Create(1f);

            Node* nodes = _nodes.UnsafePointer;
            Leaf* leaves = _leaves.UnsafePointer;

            float bestT = 1f;
            int bestIndex = -1;

            StackEntry* stack = stackalloc StackEntry[_treeDepth * (Width - 1) + 4];
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
                        break; // stale entry: a nearer hit was found since it was pushed
                    }

                    if (cur < 0)
                    {
                        // leaf block: slab test all items against the segment
                        Leaf* leaf = leaves + ~cur;
                        Vector128<float> t0 = Vector128.Multiply(Vector128.Subtract(leaf->LowerX, orgX), rdirX);
                        Vector128<float> t1 = Vector128.Multiply(Vector128.Subtract(leaf->UpperX, orgX), rdirX);
                        Vector128<float> tMin = posX ? t0 : t1;
                        Vector128<float> tMax = posX ? t1 : t0;
                        t0 = Vector128.Multiply(Vector128.Subtract(leaf->LowerY, orgY), rdirY);
                        t1 = Vector128.Multiply(Vector128.Subtract(leaf->UpperY, orgY), rdirY);
                        tMin = Vector128.Max(tMin, posY ? t0 : t1);
                        tMax = Vector128.Min(tMax, posY ? t1 : t0);
                        // accept inside the segment, but rank by the raw (unclamped) entry
                        Vector128<float> leafSeg = Vector128.LessThanOrEqual(Vector128.Max(tMin, zero), Vector128.Min(tMax, one));
                        int leafMask = (int)Vector128.BitwiseAnd(leafSeg, Vector128.LessThanOrEqual(tMin, Vector128.Create(bestT))).ExtractMostSignificantBits();
                        if (leafMask != 0)
                        {
                            for (int i = 0; i < Width; i++)
                            {
                                if ((leafMask & (1 << i)) != 0)
                                {
                                    float t = tMin.GetElement(i);
                                    if (t < bestT)
                                    {
                                        bestT = t;
                                        bestIndex = leaf->Indices[i];
                                    }
                                }
                            }
                        }
                        break;
                    }

                    // inner node: one SIMD slab test for all 4 children
                    Node* node = nodes + cur;
                    Vector128<float> s0 = Vector128.Multiply(Vector128.Subtract(node->LowerX, orgX), rdirX);
                    Vector128<float> s1 = Vector128.Multiply(Vector128.Subtract(node->UpperX, orgX), rdirX);
                    Vector128<float> sMin = posX ? s0 : s1;
                    Vector128<float> sMax = posX ? s1 : s0;
                    s0 = Vector128.Multiply(Vector128.Subtract(node->LowerY, orgY), rdirY);
                    s1 = Vector128.Multiply(Vector128.Subtract(node->UpperY, orgY), rdirY);
                    sMin = Vector128.Max(sMin, posY ? s0 : s1);
                    sMax = Vector128.Min(sMax, posY ? s1 : s0);
                    Vector128<float> nodeSeg = Vector128.LessThanOrEqual(Vector128.Max(sMin, zero), Vector128.Min(sMax, one));
                    int nodeMask = (int)Vector128.BitwiseAnd(nodeSeg, Vector128.LessThanOrEqual(sMin, Vector128.Create(bestT))).ExtractMostSignificantBits();
                    if (nodeMask == 0)
                    {
                        break;
                    }

                    // order the surviving children by raw entry distance (insertion sort of <= 4)
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

                    // descend into the nearest child in place; push the rest so the nearest
                    // remaining subtree is on top of the stack
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

            if (bestIndex >= 0)
            {
                hitIndex = bestIndex;
                hitT = bestT;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Casts a ray segment and returns on the first hit (any-hit query). Uses the exact
        /// SIMD slab test at every node; children are visited largest-subtree first, so the
        /// result never over-reports (unlike a segment-box overlap prune).
        /// </summary>
        public bool RayCastAny(Vector2 origin, Vector2 displacement)
        {
            if (_rootRef == EmptyRef)
            {
                return false;
            }

            float invX = displacement.X != 0f ? 1f / displacement.X : float.MaxValue;
            float invY = displacement.Y != 0f ? 1f / displacement.Y : float.MaxValue;
            bool posX = invX >= 0f;
            bool posY = invY >= 0f;
            Vector128<float> orgX = Vector128.Create(origin.X);
            Vector128<float> orgY = Vector128.Create(origin.Y);
            Vector128<float> rdirX = Vector128.Create(invX);
            Vector128<float> rdirY = Vector128.Create(invY);
            Vector128<float> zero = Vector128<float>.Zero;
            Vector128<float> one = Vector128.Create(1f);

            Node* nodes = _nodes.UnsafePointer;
            Leaf* leaves = _leaves.UnsafePointer;

            int* stack = stackalloc int[_treeDepth * (Width - 1) + 4];
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
                        int leafMask = (int)Vector128.LessThanOrEqual(Vector128.Max(tMin, zero), Vector128.Min(tMax, one)).ExtractMostSignificantBits();
                        if (leafMask != 0)
                        {
                            return true;
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
                    int nodeMask = (int)Vector128.LessThanOrEqual(Vector128.Max(sMin, zero), Vector128.Min(sMax, one)).ExtractMostSignificantBits();
                    if (nodeMask == 0)
                    {
                        break;
                    }

                    // push in reverse slot order so the largest subtree (slot 0) is visited first
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

        /// <summary>
        /// Finds all leaf items whose AABB overlaps <paramref name="query"/>.
        /// Results are appended to <paramref name="results"/>.
        /// </summary>
        public void OverlapAabb(in BoundingBox2D query, List<int> results)
        {
            if (_rootRef == EmptyRef)
            {
                return;
            }

            Vector128<float> qMinX = Vector128.Create(query.Min.X);
            Vector128<float> qMinY = Vector128.Create(query.Min.Y);
            Vector128<float> qMaxX = Vector128.Create(query.Max.X);
            Vector128<float> qMaxY = Vector128.Create(query.Max.Y);

            Node* nodes = _nodes.UnsafePointer;
            Leaf* leaves = _leaves.UnsafePointer;

            int* stack = stackalloc int[_treeDepth * (Width - 1) + 4];
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
                                int index = leaf->Indices[i];
                                if (index >= 0)
                                {
                                    results.Add(index);
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

        /// <summary>
        /// Finds all leaf items whose AABB contains <paramref name="point"/>.
        /// Results are appended to <paramref name="results"/>.
        /// </summary>
        public void QueryPoint(Vector2 point, List<int> results)
        {
            if (_rootRef == EmptyRef)
            {
                return;
            }

            Vector128<float> px = Vector128.Create(point.X);
            Vector128<float> py = Vector128.Create(point.Y);

            Node* nodes = _nodes.UnsafePointer;
            Leaf* leaves = _leaves.UnsafePointer;

            int* stack = stackalloc int[_treeDepth * (Width - 1) + 4];
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
                                int index = leaf->Indices[i];
                                if (index >= 0)
                                {
                                    results.Add(index);
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

            _nodes.Dispose();
            _leaves.Dispose();
            _pairs.Dispose();
            _items.Dispose();
            _isDisposed = true;
        }
    }
}
