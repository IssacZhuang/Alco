using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;

namespace Alco
{
    /// <summary>
    /// Zero-allocation visitor callback for point and AABB overlap queries on 3D bounding volume hierarchies.
    /// </summary>
    public interface IBvhAabb3DLeafVisitor
    {
        /// <summary>
        /// Visits one matching leaf payload. Return <c>true</c> to continue traversal or <c>false</c> to stop early.
        /// </summary>
        bool OnLeaf(int payload);
    }

    /// <summary>
    /// Zero-allocation visitor callback for closest-hit ray queries on 3D bounding volume hierarchies.
    /// </summary>
    public interface IBvhAabb3DRayClosestVisitor
    {
        /// <summary>
        /// Visits a candidate leaf payload whose AABB entry fraction did not exceed <paramref name="currentBestT"/>.
        /// Returns the updated maximum hit fraction (any value strictly smaller than <paramref name="currentBestT"/> tightens the ray bound).
        /// </summary>
        float OnLeaf(int payload, float entryT, float currentBestT);
    }

    /// <summary>
    /// Summary metadata produced when building a 4-wide <see cref="BvhAabb3D"/> tree.
    /// </summary>
    public readonly struct BvhAabb3DBuildResult
    {
        /// <summary>Gets the tagged root reference, or <see cref="BvhAabb3D.EmptyRef"/> when empty.</summary>
        public int RootRef { get; }

        /// <summary>Gets the exact count of internal nodes written into the builder buffer.</summary>
        public int NodeCount { get; }

        /// <summary>Gets the exact count of leaf blocks written into the builder buffer.</summary>
        public int LeafCount { get; }

        /// <summary>Gets the maximum depth of the generated tree.</summary>
        public int TreeDepth { get; }

        /// <summary>
        /// Initializes a build result descriptor.
        /// </summary>
        public BvhAabb3DBuildResult(int rootRef, int nodeCount, int leafCount, int treeDepth)
        {
            RootRef = rootRef;
            NodeCount = nodeCount;
            LeafCount = leafCount;
            TreeDepth = treeDepth;
        }
    }

    /// <summary>
    /// Reusable native scratch builder for <see cref="BvhAabb3D"/> trees.
    /// Sharing one builder across many trees eliminates per-tree scratch allocations.
    /// Not thread-safe across concurrent builds.
    /// </summary>
    public sealed unsafe class BvhAabb3DBuilder : IDisposable
    {
        private const int Width = BvhAabb3D.Width;
        private const int MaxLeafItems = BvhAabb3D.MaxLeafItems;
        private const int MaxBuildDepth = 64;

        private const int BitsPerAxis = 10;
        private const int RadixBits = 10;
        private const int RadixBuckets = 1 << RadixBits;
        private const int PassCount = (BitsPerAxis * 3 + RadixBits - 1) / RadixBits; // = 3 (30-bit Morton)

        private static readonly BoundingBox3D EmptyBox = new(new Vector3(float.MaxValue), new Vector3(float.MinValue));

        private struct Item
        {
            public BoundingBox3D Bounds;
            public int Payload;
        }

        private NativeBuffer<BvhAabb3D.Node> _nodes;
        private NativeBuffer<BvhAabb3D.Leaf> _leaves;
        private NativeBuffer<ulong> _pairs;
        private NativeBuffer<Item> _items;
        private ulong* _sorted;
        private int _nodeCount;
        private int _leafCount;
        private int _treeDepth;
        private bool _isDisposed;

        /// <summary>Gets total unmanaged scratch bytes currently retained by this builder.</summary>
        public long NativeMemoryBytes =>
            (long)_nodes.Capacity * sizeof(BvhAabb3D.Node) +
            (long)_leaves.Capacity * sizeof(BvhAabb3D.Leaf) +
            (long)_pairs.Capacity * sizeof(ulong) +
            (long)_items.Capacity * sizeof(Item);

        /// <summary>
        /// Builds a 4-wide 30-bit Morton LBVH over <paramref name="bounds"/> using sequential indices <c>0..bounds.Length-1</c> as payloads.
        /// Returned spans alias scratch storage owned by this builder and remain valid until the next build or <see cref="Dispose"/>.
        /// </summary>
        public BvhAabb3DBuildResult Build(
            ReadOnlySpan<BoundingBox3D> bounds,
            out ReadOnlySpan<BvhAabb3D.Node> nodes,
            out ReadOnlySpan<BvhAabb3D.Leaf> leaves)
        {
            return BuildCore(bounds, ReadOnlySpan<int>.Empty, hasExplicitPayloads: false, out nodes, out leaves);
        }

        /// <summary>
        /// Builds a 4-wide 30-bit Morton LBVH over <paramref name="bounds"/> with caller-supplied integer <paramref name="payloads"/>.
        /// Returned spans alias scratch storage owned by this builder and remain valid until the next build or <see cref="Dispose"/>.
        /// </summary>
        public BvhAabb3DBuildResult Build(
            ReadOnlySpan<BoundingBox3D> bounds,
            ReadOnlySpan<int> payloads,
            out ReadOnlySpan<BvhAabb3D.Node> nodes,
            out ReadOnlySpan<BvhAabb3D.Leaf> leaves)
        {
            if (payloads.Length < bounds.Length)
            {
                throw new ArgumentException("Payloads span length must be at least bounds span length.", nameof(payloads));
            }
            return BuildCore(bounds, payloads, hasExplicitPayloads: true, out nodes, out leaves);
        }

        private BvhAabb3DBuildResult BuildCore(
            ReadOnlySpan<BoundingBox3D> bounds,
            ReadOnlySpan<int> payloads,
            bool hasExplicitPayloads,
            out ReadOnlySpan<BvhAabb3D.Node> nodes,
            out ReadOnlySpan<BvhAabb3D.Leaf> leaves)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);

            int n = bounds.Length;
            if (n == 0)
            {
                nodes = ReadOnlySpan<BvhAabb3D.Node>.Empty;
                leaves = ReadOnlySpan<BvhAabb3D.Leaf>.Empty;
                return new BvhAabb3DBuildResult(BvhAabb3D.EmptyRef, 0, 0, 0);
            }

            EnsureCapacity(n);

            Vector3 sceneMin = new(float.MaxValue);
            Vector3 sceneMax = new(float.MinValue);
            for (int i = 0; i < n; i++)
            {
                Vector3 center = bounds[i].Min + bounds[i].Max;
                sceneMin = Vector3.Min(sceneMin, center);
                sceneMax = Vector3.Max(sceneMax, center);
            }

            ulong* pairs = _pairs.UnsafePointer;
            Vector3 extent = sceneMax - sceneMin;
            float invX = extent.X > 0f ? 1f / extent.X : 0f;
            float invY = extent.Y > 0f ? 1f / extent.Y : 0f;
            float invZ = extent.Z > 0f ? 1f / extent.Z : 0f;
            for (int i = 0; i < n; i++)
            {
                Vector3 center = bounds[i].Min + bounds[i].Max;
                uint code = MortonCode(
                    (center.X - sceneMin.X) * invX,
                    (center.Y - sceneMin.Y) * invY,
                    (center.Z - sceneMin.Z) * invZ);
                pairs[i] = ((ulong)code << 32) | (uint)i;
            }

            _sorted = RadixSort(pairs, pairs + n, n);

            Item* items = _items.UnsafePointer;
            if (hasExplicitPayloads)
            {
                for (int i = 0; i < n; i++)
                {
                    int srcIndex = (int)(uint)_sorted[i];
                    int payload = payloads[srcIndex];
                    if (payload == BvhAabb3D.EmptyLeafPayload)
                    {
                        throw new ArgumentException("Payload cannot equal BvhAabb3D.EmptyLeafPayload.", nameof(payloads));
                    }
                    items[i] = new Item { Bounds = bounds[srcIndex], Payload = payload };
                }
            }
            else
            {
                for (int i = 0; i < n; i++)
                {
                    int srcIndex = (int)(uint)_sorted[i];
                    items[i] = new Item { Bounds = bounds[srcIndex], Payload = srcIndex };
                }
            }

            _nodeCount = 0;
            _leafCount = 0;
            _treeDepth = 0;
            int rootRef = BuildRange(0, n, 1, out _);
            _sorted = null;

            nodes = _nodeCount > 0
                ? new ReadOnlySpan<BvhAabb3D.Node>(_nodes.UnsafePointer, _nodeCount)
                : ReadOnlySpan<BvhAabb3D.Node>.Empty;
            leaves = _leafCount > 0
                ? new ReadOnlySpan<BvhAabb3D.Leaf>(_leaves.UnsafePointer, _leafCount)
                : ReadOnlySpan<BvhAabb3D.Leaf>.Empty;

            return new BvhAabb3DBuildResult(rootRef, _nodeCount, _leafCount, _treeDepth);
        }

        private void EnsureCapacity(int count)
        {
            if (_nodes.Capacity < count)
            {
                _nodes.Dispose();
                _nodes = new NativeBuffer<BvhAabb3D.Node>(count);
            }
            if (_leaves.Capacity < count)
            {
                _leaves.Dispose();
                _leaves = new NativeBuffer<BvhAabb3D.Leaf>(count);
            }
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
        }

        private int BuildRange(int start, int end, int depth, out BoundingBox3D bounds)
        {
            if (end - start <= MaxLeafItems)
            {
                return EmitLeaf(start, end, depth, out bounds);
            }

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

            BoundingBox3D b0 = EmptyBox, b1 = EmptyBox, b2 = EmptyBox, b3 = EmptyBox;
            int c0 = BuildRange(rStart[0], rEnd[0], depth + 1, out b0);
            int c1 = BuildRange(rStart[1], rEnd[1], depth + 1, out b1);
            int c2 = rangeCount > 2 ? BuildRange(rStart[2], rEnd[2], depth + 1, out b2) : BvhAabb3D.EmptyRef;
            int c3 = rangeCount > 3 ? BuildRange(rStart[3], rEnd[3], depth + 1, out b3) : BvhAabb3D.EmptyRef;

            int index = _nodeCount++;
            BvhAabb3D.Node* node = _nodes.UnsafePointer + index;
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

        private int EmitLeaf(int start, int end, int depth, out BoundingBox3D bounds)
        {
            Item* items = _items.UnsafePointer;
            BoundingBox3D b0 = items[start].Bounds;
            BoundingBox3D b1 = end - start > 1 ? items[start + 1].Bounds : EmptyBox;
            BoundingBox3D b2 = end - start > 2 ? items[start + 2].Bounds : EmptyBox;
            BoundingBox3D b3 = end - start > 3 ? items[start + 3].Bounds : EmptyBox;

            int leafIndex = _leafCount++;
            BvhAabb3D.Leaf* leaf = _leaves.UnsafePointer + leafIndex;
            leaf->LowerX = Vector128.Create(b0.Min.X, b1.Min.X, b2.Min.X, b3.Min.X);
            leaf->UpperX = Vector128.Create(b0.Max.X, b1.Max.X, b2.Max.X, b3.Max.X);
            leaf->LowerY = Vector128.Create(b0.Min.Y, b1.Min.Y, b2.Min.Y, b3.Min.Y);
            leaf->UpperY = Vector128.Create(b0.Max.Y, b1.Max.Y, b2.Max.Y, b3.Max.Y);
            leaf->LowerZ = Vector128.Create(b0.Min.Z, b1.Min.Z, b2.Min.Z, b3.Min.Z);
            leaf->UpperZ = Vector128.Create(b0.Max.Z, b1.Max.Z, b2.Max.Z, b3.Max.Z);
            leaf->Indices[0] = items[start].Payload;
            leaf->Indices[1] = end - start > 1 ? items[start + 1].Payload : BvhAabb3D.EmptyLeafPayload;
            leaf->Indices[2] = end - start > 2 ? items[start + 2].Payload : BvhAabb3D.EmptyLeafPayload;
            leaf->Indices[3] = end - start > 3 ? items[start + 3].Payload : BvhAabb3D.EmptyLeafPayload;

            if (depth > _treeDepth)
            {
                _treeDepth = depth;
            }

            bounds = BoundingBox3D.Merge(BoundingBox3D.Merge(b0, b1), BoundingBox3D.Merge(b2, b3));
            return ~leafIndex;
        }

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

        /// <summary>
        /// Frees all unmanaged scratch buffers while keeping this builder reusable for future builds.
        /// </summary>
        public void Clear()
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            _nodes.Dispose();
            _nodes = default;
            _leaves.Dispose();
            _leaves = default;
            _pairs.Dispose();
            _pairs = default;
            _items.Dispose();
            _items = default;
            _sorted = null;
            _nodeCount = 0;
            _leafCount = 0;
            _treeDepth = 0;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            Clear();
            _isDisposed = true;
        }
    }
}
