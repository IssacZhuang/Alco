using System;

namespace Alco
{
    /// <summary>
    /// A node in a 2D bounding volume hierarchy.
    /// The layout is part of the build contract between <see cref="NativeBvh2D"/> and <see cref="IBvhBuilder2D"/>:
    /// leaf nodes occupy [0, leafCount) of the node buffer, internal nodes are appended after the leaves,
    /// and child indices address the whole buffer.
    /// </summary>
    public struct BvhNode2D
    {
        /// <summary>
        /// The index of the left child node, or -1 if none.
        /// </summary>
        public int Left;

        /// <summary>
        /// The index of the right child node, or -1 if none.
        /// </summary>
        public int Right;

        /// <summary>
        /// The bounding box of this node.
        /// </summary>
        public BoundingBox2D Bounds;

        /// <summary>
        /// The collider associated with this node if it is a leaf.
        /// </summary>
        public ColliderRef2D Collider;

        /// <summary>
        /// Gets a value indicating whether this node is a leaf node.
        /// </summary>
        public bool IsLeaf => Collider.HasCollider;
    }
}
