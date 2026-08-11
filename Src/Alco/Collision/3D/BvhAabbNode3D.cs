using System;
using System.Runtime.CompilerServices;

namespace Alco
{
    /// <summary>
    /// A flat AABB-only node for <see cref="BvhAabb3D"/>.
    /// <para>
    /// Unlike <see cref="BvhNode3D"/> this carries no collider pointer — just the bounding box,
    /// child indices, and a user index. The struct is 36 bytes (24 bounds + 3×int) versus
    /// ~48+ bytes for the collider-based node, giving better cache utilisation during traversal.
    /// </para>
    /// Layout contract (same as the collider BVH): leaves occupy [0, leafCount) of the node
    /// buffer; internal nodes are appended after the leaves; child indices address the whole buffer.
    /// </summary>
    public struct BvhAabbNode3D
    {
        /// <summary>Bounding box of this node (union of children for internal nodes).</summary>
        public BoundingBox3D Bounds;

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
