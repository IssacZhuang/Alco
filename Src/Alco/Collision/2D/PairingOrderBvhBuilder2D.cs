using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Alco
{
    /// <summary>
    /// An <see cref="IBvhBuilder2D"/> that fully preserves the caller's input order: leaf blocks
    /// and child lanes follow the collider sequence exactly, so the tree is a balanced 4-ary
    /// range tree over the input and the build never reorders anything. Use it when the caller
    /// maintains the collider order itself (for example incrementally re-sorting a persistent
    /// collection every tick for spatial coherence) and only needs the BVH to accelerate
    /// queries; tree quality then depends entirely on the input order. The historical name
    /// refers to the original binary pairwise-merge builder with the same order contract.
    /// The builder keeps no per-build scratch memory, so one instance can serve any number of
    /// trees (builds themselves must not run concurrently, as with any builder).
    /// </summary>
    public unsafe class PairingOrderBvhBuilder2D : IBvhBuilder2D
    {
        private const int Width = 4;
        private const int MaxLeafItems = 4;

        // an inverted box (min greater than max) fails every slab/overlap/contains mask, so unused
        // lanes never produce traversal work; merging it into any real box returns the real box
        private static readonly BoundingBox2D EmptyBox = new(new Vector2(float.MaxValue), new Vector2(float.MinValue));

        /// <summary>
        /// A shared instance of the stateless pairing-order builder.
        /// </summary>
        public static readonly PairingOrderBvhBuilder2D Shared = new();

        private int _nodeCount;
        private int _leafCount;
        private int _treeDepth;

        /// <inheritdoc/>
        public BvhBuildResult2D Build(ReadOnlySpan<ColliderRef2D> colliders, Span<BvhNode2D> nodes, Span<BvhLeaf2D> leaves)
        {
            int n = colliders.Length;
            if (n == 0)
            {
                return new BvhBuildResult2D { Root = BvhNode2D.EmptyChild };
            }

            BvhNode2D* nodePtr = (BvhNode2D*)Unsafe.AsPointer(ref MemoryMarshal.GetReference(nodes));
            BvhLeaf2D* leafPtr = (BvhLeaf2D*)Unsafe.AsPointer(ref MemoryMarshal.GetReference(leaves));

            _nodeCount = 0;
            _leafCount = 0;
            _treeDepth = 0;
            int root = BuildRange(nodePtr, leafPtr, colliders, 0, n, 1, out _);

            return new BvhBuildResult2D
            {
                Root = root,
                NodeCount = _nodeCount,
                LeafCount = _leafCount,
                TreeDepth = _treeDepth,
            };
        }

        // splits the input range [start, end) into four contiguous chunks as equal as possible
        // and recurses; children and leaf lanes keep the input order, so the only structural
        // decision is the chunk sizing
        private int BuildRange(BvhNode2D* nodes, BvhLeaf2D* leaves, ReadOnlySpan<ColliderRef2D> colliders,
            int start, int end, int depth, out BoundingBox2D bounds)
        {
            if (end - start <= MaxLeafItems)
            {
                return EmitLeaf(leaves, colliders, start, end, depth, out bounds);
            }

            int n = end - start;
            int q = n / Width;
            int r = n % Width;
            // the first r chunks take one extra item; n >= 5 guarantees every chunk is non-empty
            int b1 = start + q + (r > 0 ? 1 : 0);
            int b2 = b1 + q + (r > 1 ? 1 : 0);
            int b3 = b2 + q + (r > 2 ? 1 : 0);

            int c0 = BuildRange(nodes, leaves, colliders, start, b1, depth + 1, out BoundingBox2D b0);
            int c1 = BuildRange(nodes, leaves, colliders, b1, b2, depth + 1, out BoundingBox2D b1Bounds);
            int c2 = BuildRange(nodes, leaves, colliders, b2, b3, depth + 1, out BoundingBox2D b2Bounds);
            int c3 = BuildRange(nodes, leaves, colliders, b3, end, depth + 1, out BoundingBox2D b3Bounds);

            int index = _nodeCount++;
            BvhNode2D* node = nodes + index;
            node->LowerX = Vector128.Create(b0.Min.X, b1Bounds.Min.X, b2Bounds.Min.X, b3Bounds.Min.X);
            node->UpperX = Vector128.Create(b0.Max.X, b1Bounds.Max.X, b2Bounds.Max.X, b3Bounds.Max.X);
            node->LowerY = Vector128.Create(b0.Min.Y, b1Bounds.Min.Y, b2Bounds.Min.Y, b3Bounds.Min.Y);
            node->UpperY = Vector128.Create(b0.Max.Y, b1Bounds.Max.Y, b2Bounds.Max.Y, b3Bounds.Max.Y);
            node->Children[0] = c0;
            node->Children[1] = c1;
            node->Children[2] = c2;
            node->Children[3] = c3;

            bounds = BoundingBox2D.Merge(BoundingBox2D.Merge(b0, b1Bounds), BoundingBox2D.Merge(b2Bounds, b3Bounds));
            return index;
        }

        private int EmitLeaf(BvhLeaf2D* leaves, ReadOnlySpan<ColliderRef2D> colliders, int start, int end,
            int depth, out BoundingBox2D bounds)
        {
            int leafIndex = _leafCount++;
            BvhLeaf2D* leaf = leaves + leafIndex;

            BoundingBox2D b0 = colliders[start].GetBoundingBox();
            BoundingBox2D b1 = end - start > 1 ? colliders[start + 1].GetBoundingBox() : EmptyBox;
            BoundingBox2D b2 = end - start > 2 ? colliders[start + 2].GetBoundingBox() : EmptyBox;
            BoundingBox2D b3 = end - start > 3 ? colliders[start + 3].GetBoundingBox() : EmptyBox;
            leaf->LowerX = Vector128.Create(b0.Min.X, b1.Min.X, b2.Min.X, b3.Min.X);
            leaf->UpperX = Vector128.Create(b0.Max.X, b1.Max.X, b2.Max.X, b3.Max.X);
            leaf->LowerY = Vector128.Create(b0.Min.Y, b1.Min.Y, b2.Min.Y, b3.Min.Y);
            leaf->UpperY = Vector128.Create(b0.Max.Y, b1.Max.Y, b2.Max.Y, b3.Max.Y);
            leaf->C0 = colliders[start];
            leaf->C1 = end - start > 1 ? colliders[start + 1] : default;
            leaf->C2 = end - start > 2 ? colliders[start + 2] : default;
            leaf->C3 = end - start > 3 ? colliders[start + 3] : default;

            if (depth > _treeDepth)
            {
                _treeDepth = depth;
            }

            // empty lanes are the identity under Merge, so this is the union of the real items
            bounds = BoundingBox2D.Merge(BoundingBox2D.Merge(b0, b1), BoundingBox2D.Merge(b2, b3));
            return BvhNode2D.EncodeLeaf(leafIndex);
        }
    }
}
