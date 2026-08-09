using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Alco
{
    /// <summary>
    /// An <see cref="IBvhBuilder3D"/> that builds a binary radix tree over Morton codes (LBVH style,
    /// Karras 2012). Leaf centroids are mapped to 30-bit Morton codes, sorted with an LSD radix sort,
    /// and the tree is split at the highest differing code bit, so the topology adapts to the spatial
    /// density of the colliders instead of depending on the input order.
    /// All scratch memory is owned by the instance and reused across builds (zero managed allocations
    /// once the buffers have reached their high-water mark).
    /// </summary>
    public class MortonBvhBuilder3D : IBvhBuilder3D, IDisposable
    {
        private const int BitsPerAxis = 10;
        private const int RadixBits = 10;
        private const int RadixBuckets = 1 << RadixBits;
        private const int PassCount = (BitsPerAxis * 3 + RadixBits - 1) / RadixBits;

        // ping-pong buffers for the radix sort: (mortonCode << 32) | leafIndex pairs
        private NativeBuffer<ulong> _pairs;
        // final leaf order (slot -> source leaf index), also used by the in-place gather
        private NativeBuffer<int> _perm;
        private bool _isDisposed;

        /// <inheritdoc/>
        public unsafe void Build(ReadOnlySpan<ColliderRef3D> colliders, Span<BvhNode3D> nodes,
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
            Vector3 sceneMin = new Vector3(float.MaxValue);
            Vector3 sceneMax = new Vector3(float.MinValue);
            for (int i = 0; i < n; i++)
            {
                ColliderRef3D collider = colliders[i];
                BoundingBox3D bounds = collider.GetBoundingBox();
                nodes[i] = new BvhNode3D
                {
                    Left = -1,
                    Right = -1,
                    Collider = collider,
                    Bounds = bounds,
                };

                Vector3 center = bounds.Min + bounds.Max; // 2x center, avoids a division per leaf
                sceneMin = Vector3.Min(sceneMin, center);
                sceneMax = Vector3.Max(sceneMax, center);
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
            Vector3 extent = sceneMax - sceneMin;
            float invX = extent.X > 0 ? 1f / extent.X : 0f;
            float invY = extent.Y > 0 ? 1f / extent.Y : 0f;
            float invZ = extent.Z > 0 ? 1f / extent.Z : 0f;

            for (int i = 0; i < n; i++)
            {
                BoundingBox3D bounds = nodes[i].Bounds;
                Vector3 center = bounds.Min + bounds.Max;
                uint code = MortonCode(
                    (center.X - sceneMin.X) * invX,
                    (center.Y - sceneMin.Y) * invY,
                    (center.Z - sceneMin.Z) * invZ);
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
            root = Emit(nodes, sorted, 0, n, n, ref internalCounter, ref maxDepth, 1);

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
        private static unsafe int Emit(Span<BvhNode3D> nodes, ulong* sorted, int start, int end, int leafCount,
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

            int left = Emit(nodes, sorted, start, split, leafCount, ref internalCounter, ref maxDepth, depth + 1);
            int right = Emit(nodes, sorted, split, end, leafCount, ref internalCounter, ref maxDepth, depth + 1);

            int index = internalCounter++;
            nodes[index] = new BvhNode3D
            {
                Left = left,
                Right = right,
                Bounds = BoundingBox3D.Merge(nodes[left].Bounds, nodes[right].Bounds),
            };
            return index;
        }

        // LSD radix sort over the 30-bit morton codes; returns a pointer to the sorted half
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
        private static unsafe void ApplyPermutation(Span<BvhNode3D> leaves, int* perm, int n)
        {
            for (int i = 0; i < n; i++)
            {
                if (perm[i] < 0)
                {
                    continue;
                }

                BvhNode3D saved = leaves[i];
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
