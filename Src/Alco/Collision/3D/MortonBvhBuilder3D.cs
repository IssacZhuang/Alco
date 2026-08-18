using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Alco
{
    /// <summary>
    /// An <see cref="IBvhBuilder3D"/> producing a 4-wide BVH over Morton codes (LBVH style,
    /// Karras 2012). Leaf centroids are mapped to 30-bit Morton codes, sorted with an LSD radix
    /// sort, and each internal node is formed with a falling split: the largest remaining range
    /// is split at the highest differing code bit until the node has up to four children, which
    /// are then ordered by descending item count so any-hit style queries meet the bigger
    /// subtree first. Subtree bounds are computed bottom-up while the recursion unwinds, so a
    /// build touches every collider a constant number of times.
    /// All scratch memory is owned by the instance and reused across builds (zero managed
    /// allocations once the buffers have reached their high-water mark).
    /// </summary>
    public unsafe class MortonBvhBuilder3D : IBvhBuilder3D, IDisposable
    {
        private const int Width = 4;
        private const int MaxLeafItems = 4;
        private const int MaxBuildDepth = 96;

        private const int BitsPerAxis = 10;
        private const int RadixBits = 10;
        private const int RadixBuckets = 1 << RadixBits;
        private const int PassCount = (BitsPerAxis * 3 + RadixBits - 1) / RadixBits;

        // an inverted box (min greater than max) fails every slab/overlap/contains mask, so unused
        // lanes never produce traversal work; merging it into any real box returns the real box
        private static readonly BoundingBox3D EmptyBox = new(new Vector3(float.MaxValue), new Vector3(float.MinValue));

        private struct Item
        {
            public BoundingBox3D Bounds;
            public ColliderRef3D Collider;
        }

        // ping-pong buffers for the radix sort: (mortonCode << 32) | sourceIndex pairs
        private NativeBuffer<ulong> _pairs;
        private NativeBuffer<Item> _items;       // input order
        private NativeBuffer<Item> _sortedItems; // Morton order
        private ulong* _sorted;                  // radix output, valid during Build only
        private int _nodeCount;
        private int _leafCount;
        private int _treeDepth;
        private bool _isDisposed;

        /// <inheritdoc/>
        public BvhBuildResult3D Build(ReadOnlySpan<ColliderRef3D> colliders, Span<BvhNode3D> nodes, Span<BvhLeaf3D> leaves)
        {
            int n = colliders.Length;
            if (n == 0)
            {
                return new BvhBuildResult3D { Root = BvhNode3D.EmptyChild };
            }

            EnsureCapacity(n);

            BvhNode3D* nodePtr = (BvhNode3D*)Unsafe.AsPointer(ref MemoryMarshal.GetReference(nodes));
            BvhLeaf3D* leafPtr = (BvhLeaf3D*)Unsafe.AsPointer(ref MemoryMarshal.GetReference(leaves));

            // items in input order, tracking the scene bounds of the centroids
            Item* items = _items.UnsafePointer;
            Vector3 sceneMin = new(float.MaxValue);
            Vector3 sceneMax = new(float.MinValue);
            for (int i = 0; i < n; i++)
            {
                BoundingBox3D b = colliders[i].GetBoundingBox();
                items[i] = new Item { Bounds = b, Collider = colliders[i] };
                Vector3 center = b.Min + b.Max; // 2x center, avoids a division per leaf
                sceneMin = Vector3.Min(sceneMin, center);
                sceneMax = Vector3.Max(sceneMax, center);
            }

            // morton codes of the centroids, normalized to the scene bounds
            ulong* pairs = _pairs.UnsafePointer;
            Vector3 extent = sceneMax - sceneMin;
            float invX = extent.X > 0 ? 1f / extent.X : 0f;
            float invY = extent.Y > 0 ? 1f / extent.Y : 0f;
            float invZ = extent.Z > 0 ? 1f / extent.Z : 0f;
            for (int i = 0; i < n; i++)
            {
                Vector3 center = items[i].Bounds.Min + items[i].Bounds.Max;
                uint code = MortonCode(
                    (center.X - sceneMin.X) * invX,
                    (center.Y - sceneMin.Y) * invY,
                    (center.Z - sceneMin.Z) * invZ);
                pairs[i] = ((ulong)code << 32) | (uint)i;
            }

            _sorted = RadixSort(pairs, pairs + n, n);

            Item* sortedItems = _sortedItems.UnsafePointer;
            for (int i = 0; i < n; i++)
            {
                sortedItems[i] = items[(int)(uint)_sorted[i]];
            }

            _nodeCount = 0;
            _leafCount = 0;
            _treeDepth = 0;
            int root = BuildRange(nodePtr, leafPtr, sortedItems, 0, n, 1, out _);
            _sorted = null;

            return new BvhBuildResult3D
            {
                Root = root,
                NodeCount = _nodeCount,
                LeafCount = _leafCount,
                TreeDepth = _treeDepth,
            };
        }

        /// <summary>
        /// Releases the scratch buffers used by the builder.
        /// </summary>
        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            _pairs.Dispose();
            _items.Dispose();
            _sortedItems.Dispose();
            _isDisposed = true;
        }

        private void EnsureCapacity(int count)
        {
            if (_pairs.Capacity < count * 2)
            {
                _pairs.Dispose();
                _pairs = new NativeBuffer<ulong>(count * 2);
            }
            if (_items.Capacity < count)
            {
                _items.Dispose();
                _items = new NativeBuffer<Item>(count);
            }
            if (_sortedItems.Capacity < count)
            {
                _sortedItems.Dispose();
                _sortedItems = new NativeBuffer<Item>(count);
            }
        }

        // recursively splits the sorted item range [start, end); emits leaf blocks and internal
        // nodes bottom-up and returns the tagged subtree reference together with its bounds
        private int BuildRange(BvhNode3D* nodes, BvhLeaf3D* leaves, Item* items, int start, int end, int depth, out BoundingBox3D bounds)
        {
            if (end - start <= MaxLeafItems)
            {
                return EmitLeaf(leaves, items, start, end, depth, out bounds);
            }

            // falling split: repeatedly split the largest subrange with the binary Morton
            // split until the node has up to Width children
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
                    break;
                }

                int s = rStart[best];
                int e = rEnd[best];
                int split = depth < MaxBuildDepth ? FindSplit(s, e) : (s + e) / 2;
                rEnd[best] = split;
                rStart[rangeCount] = split;
                rEnd[rangeCount] = e;
                rangeCount++;
            }

            // children ordered by descending item count: any-hit / collector queries expect
            // the bigger subtree first
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

            int c0 = BuildRange(nodes, leaves, items, rStart[0], rEnd[0], depth + 1, out BoundingBox3D b0);
            int c1 = BuildRange(nodes, leaves, items, rStart[1], rEnd[1], depth + 1, out BoundingBox3D b1);
            int c2;
            int c3;
            BoundingBox3D b2;
            BoundingBox3D b3;
            if (rangeCount > 2)
            {
                c2 = BuildRange(nodes, leaves, items, rStart[2], rEnd[2], depth + 1, out b2);
            }
            else
            {
                c2 = BvhNode3D.EmptyChild;
                b2 = EmptyBox;
            }
            if (rangeCount > 3)
            {
                c3 = BuildRange(nodes, leaves, items, rStart[3], rEnd[3], depth + 1, out b3);
            }
            else
            {
                c3 = BvhNode3D.EmptyChild;
                b3 = EmptyBox;
            }

            int index = _nodeCount++;
            BvhNode3D* node = nodes + index;
            node->LowerX = Vector128.Create(b0.Min.X, b1.Min.X, b2.Min.X, b3.Min.X);
            node->UpperX = Vector128.Create(b0.Max.X, b1.Max.X, b2.Max.X, b3.Max.X);
            node->LowerY = Vector128.Create(b0.Min.Y, b1.Min.Y, b2.Min.Y, b3.Min.Y);
            node->UpperY = Vector128.Create(b0.Max.Y, b1.Max.Y, b2.Max.Y, b3.Max.Y);
            node->LowerZ = Vector128.Create(b0.Min.Z, b1.Min.Z, b2.Min.Z, b3.Min.Z);
            node->UpperZ = Vector128.Create(b0.Max.Z, b1.Max.Z, b2.Max.Z, b3.Max.Z);
            node->Children[0] = c0;
            node->Children[1] = c1;
            node->Children[2] = c2;
            node->Children[3] = c3;

            bounds = BoundingBox3D.Merge(BoundingBox3D.Merge(b0, b1), BoundingBox3D.Merge(b2, b3));
            return index;
        }

        private int EmitLeaf(BvhLeaf3D* leaves, Item* items, int start, int end, int depth, out BoundingBox3D bounds)
        {
            int leafIndex = _leafCount++;
            BvhLeaf3D* leaf = leaves + leafIndex;

            BoundingBox3D b0 = items[start].Bounds;
            BoundingBox3D b1 = end - start > 1 ? items[start + 1].Bounds : EmptyBox;
            BoundingBox3D b2 = end - start > 2 ? items[start + 2].Bounds : EmptyBox;
            BoundingBox3D b3 = end - start > 3 ? items[start + 3].Bounds : EmptyBox;
            leaf->LowerX = Vector128.Create(b0.Min.X, b1.Min.X, b2.Min.X, b3.Min.X);
            leaf->UpperX = Vector128.Create(b0.Max.X, b1.Max.X, b2.Max.X, b3.Max.X);
            leaf->LowerY = Vector128.Create(b0.Min.Y, b1.Min.Y, b2.Min.Y, b3.Min.Y);
            leaf->UpperY = Vector128.Create(b0.Max.Y, b1.Max.Y, b2.Max.Y, b3.Max.Y);
            leaf->LowerZ = Vector128.Create(b0.Min.Z, b1.Min.Z, b2.Min.Z, b3.Min.Z);
            leaf->UpperZ = Vector128.Create(b0.Max.Z, b1.Max.Z, b2.Max.Z, b3.Max.Z);
            leaf->C0 = items[start].Collider;
            leaf->C1 = end - start > 1 ? items[start + 1].Collider : default;
            leaf->C2 = end - start > 2 ? items[start + 2].Collider : default;
            leaf->C3 = end - start > 3 ? items[start + 3].Collider : default;

            if (depth > _treeDepth)
            {
                _treeDepth = depth;
            }

            // empty lanes are the identity under Merge, so this is the union of the real items
            bounds = BoundingBox3D.Merge(BoundingBox3D.Merge(b0, b1), BoundingBox3D.Merge(b2, b3));
            return BvhNode3D.EncodeLeaf(leafIndex);
        }

        // first position in (start, end) whose code has the highest differing bit set; ranges of
        // identical codes fall back to a midpoint split
        private int FindSplit(int start, int end)
        {
            uint first = (uint)(_sorted[start] >> 32);
            uint last = (uint)(_sorted[end - 1] >> 32);
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

        // LSD radix sort over the 30-bit morton codes; returns a pointer to the sorted half
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
        private static uint MortonCode(float x, float y, float z)
        {
            const float scale = (1 << BitsPerAxis) - 1;
            uint xi = (uint)Math.Min(Math.Max(x * scale, 0f), scale);
            uint yi = (uint)Math.Min(Math.Max(y * scale, 0f), scale);
            uint zi = (uint)Math.Min(Math.Max(z * scale, 0f), scale);
            return (Part1By2(xi) << 2) | (Part1By2(yi) << 1) | Part1By2(zi);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint Part1By2(uint v)
        {
            v = (v | (v << 16)) & 0x030000FFu;
            v = (v | (v << 8)) & 0x0300F00Fu;
            v = (v | (v << 4)) & 0x030C30C3u;
            v = (v | (v << 2)) & 0x09249249u;
            return v;
        }
    }
}
