using System;
using System.Runtime.CompilerServices;

namespace Alco
{
    /// <summary>
    /// A flat AABB-only node for <see cref="BvhAabb2D"/>.
    /// <para>
    /// Unlike <see cref="BvhNode2D"/> this carries no collider pointer — just the bounding box,
    /// child indices, and a user index. The struct is 28 bytes (16 bounds + 3×int) versus
    /// ~40+ bytes for the collider-based node, giving better cache utilisation during traversal.
    /// </para>
    /// Layout contract (same as the collider BVH): leaves occupy [0, leafCount) of the node
    /// buffer; internal nodes are appended after the leaves; child indices address the whole buffer.
    /// </summary>
    public struct BvhAabbNode2D
    {
        /// <summary>Bounding box of this node (union of children for internal nodes).</summary>
        public BoundingBox2D Bounds;

        /// <summary>Left child index, or -1 for a leaf.</summary>
        public int Left;

        /// <summary>Right child index, or -1 for a leaf.</summary>
        public int Right;

        /// <summary>
        /// User index for leaf nodes (the original input index of the AABB).
        /// -1 for internal nodes.
        /// </summary>
        public int Index;

        /// <summary>True when this node is a leaf (has no children).</summary>
        public bool IsLeaf => Left < 0;
    }
}
