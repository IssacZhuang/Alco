using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Alco
{
    /// <summary>
    /// An <see cref="IBvhBuilder2D"/> that builds a binary radix tree over Morton codes (LBVH style,
    /// Karras 2012). Leaf centroids are mapped to 20-bit Morton codes, sorted with an LSD radix sort,
    /// and the tree is split at the highest differing code bit, so the topology adapts to the spatial
    /// density of the colliders instead of depending on the input order.
    /// All scratch memory is owned by the instance and reused across builds (zero managed allocations
    /// once the buffers have reached their high-water mark).
    /// </summary>
    public class MortonBvhBuilder2D : IBvhBuilder2D, IDisposable
    {
        private const int BitsPerAxis = 10;
        private const int RadixBits = 10;
        private const int RadixBuckets = 1 << RadixBits;
        private const int PassCount = (BitsPerAxis * 2 + RadixBits - 1) / RadixBits;

        // ping-pong buffers for the radix sort: (mortonCode << 32) | leafIndex pairs
        private NativeBuffer<ulong> _pairs;
        // final leaf order (slot -> source leaf index), also used by the in-place gather
        private NativeBuffer<int> _perm;
        private bool _isDisposed;

        /// <inheritdoc/>
        public unsafe void Build(ReadOnlySpan<ColliderRef2D> colliders, Span<BvhNode2D> nodes,
                                 out int nodeCount, out int root, out int treeDepth)
        {
            int n = colliders.Length;

            if (n == 0)
            {
                nodeCount = 0;
                root = -1;
                treeDepth = 0;
                return;
            }

            EnsureCapacity(n);

            // leaves in input order, tracking the scene bounds of the centroids
            Vector2 sceneMin = new Vector2(float.MaxValue);
            Vector2 sceneMax = new Vector2(float.MinValue);
            for (int i = 0; i < n; i++)
            {
                ColliderRef2D collider = colliders[i];
                BoundingBox2D bounds = collider.GetBoundingBox();
                nodes[i] = new BvhNode2D
                {
                    Left = -1,
                    Right = -1,
                    Collider = collider,
                    Bounds = bounds,
                };

                Vector2 center = bounds.Min + bounds.Max; // 2x center, avoids a division per leaf
                sceneMin = Vector2.Min(sceneMin, center);
                sceneMax = Vector2.Max(sceneMax, center);
            }

            if (n == 1)
            {
                nodeCount = 1;
                root = 0;
                treeDepth = 1;
                return;
            }

            // morton codes of the centroids, normalized to the scene bounds
            ulong* pairs = _pairs.UnsafePointer;
            Vector2 extent = sceneMax - sceneMin;
            float invX = extent.X > 0 ? 1f / extent.X : 0f;
            float invY = extent.Y > 0 ? 1f / extent.Y : 0f;

            for (int i = 0; i < n; i++)
            {
                BoundingBox2D bounds = nodes[i].Bounds;
                Vector2 center = bounds.Min + bounds.Max;
                uint code = MortonCode(
                    (center.X - sceneMin.X) * invX,
                    (center.Y - sceneMin.Y) * invY);
                pairs[i] = ((ulong)code << 32) | (uint)i;
            }

            ulong* sorted = RadixSort(pairs, _pairs.UnsafePointer + n, n);

            // final leaf order and in-place gather of the leaves
            int* perm = _perm.UnsafePointer;
            for (int i = 0; i < n; i++)
            {
                perm[i] = (int)(uint)sorted[i];
            }
            ApplyPermutation(nodes, perm, n);

            // binary radix tree: split each range at the highest differing code bit
            int internalCounter = n;
            int maxDepth = 1;
            root = Emit(nodes, sorted, 0, n, ref internalCounter, ref maxDepth, 1);

            nodeCount = internalCounter;
            treeDepth = maxDepth;
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
            _perm.Dispose();
            _isDisposed = true;
        }

        private void EnsureCapacity(int leafCount)
        {
            if (_pairs.Capacity < leafCount * 2)
            {
                _pairs.Dispose();
                _pairs = new NativeBuffer<ulong>(leafCount * 2);
            }

            if (_perm.Capacity < leafCount)
            {
                _perm.Dispose();
                _perm = new NativeBuffer<int>(leafCount);
            }
        }

        // recursively splits the sorted code range [start, end), emitting internal nodes bottom-up;
        // returns the node index of the (sub)tree root
        private static unsafe int Emit(Span<BvhNode2D> nodes, ulong* sorted, int start, int end,
                                       ref int internalCounter, ref int maxDepth, int depth)
        {
            if (end - start == 1)
            {
                if (depth > maxDepth)
                {
                    maxDepth = depth;
                }
                return start; // leaf slot
            }

            uint first = (uint)(sorted[start] >> 32);
            uint last = (uint)(sorted[end - 1] >> 32);

            int split;
            uint xor = first ^ last;
            if (xor == 0)
            {
                // identical codes across the range: fall back to a midpoint split
                split = (start + end) / 2;
            }
            else
            {
                // first position in (start, end) whose code has the highest differing bit set
                uint splitBit = 0x80000000u >> BitOperations.LeadingZeroCount(xor);
                int lo = start + 1;
                int hi = end - 1;
                while (lo < hi)
                {
                    int mid = (lo + hi) / 2;
                    if (((uint)(sorted[mid] >> 32) & splitBit) == 0)
                    {
                        lo = mid + 1;
                    }
                    else
                    {
                        hi = mid;
                    }
                }
                split = lo;
            }

            int left = Emit(nodes, sorted, start, split, ref internalCounter, ref maxDepth, depth + 1);
            int right = Emit(nodes, sorted, split, end, ref internalCounter, ref maxDepth, depth + 1);

            int index = internalCounter++;
            nodes[index] = new BvhNode2D
            {
                Left = left,
                Right = right,
                Bounds = BoundingBox2D.Merge(nodes[left].Bounds, nodes[right].Bounds),
            };
            return index;
        }

        // LSD radix sort over the 20-bit morton codes; returns a pointer to the sorted half
        private static unsafe ulong* RadixSort(ulong* src, ulong* dst, int n)
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
                    counts[(src[i] >> shift) & (RadixBuckets - 1)]++;
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
                    dst[counts[(pair >> shift) & (RadixBuckets - 1)]++] = pair;
                }

                ulong* tmp = src;
                src = dst;
                dst = tmp;
            }

            return src;
        }

        // gathers the leaves into their final slots following perm (slot -> source), in place;
        // perm entries are consumed as visit markers
        private static unsafe void ApplyPermutation(Span<BvhNode2D> leaves, int* perm, int n)
        {
            for (int i = 0; i < n; i++)
            {
                if (perm[i] < 0)
                {
                    continue;
                }

                BvhNode2D saved = leaves[i];
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
            v = (v | (v << 8)) & 0x00ff00ffu;
            v = (v | (v << 4)) & 0x0f0f0f0fu;
            v = (v | (v << 2)) & 0x33333333u;
            v = (v | (v << 1)) & 0x55555555u;
            return v;
        }
    }
}
