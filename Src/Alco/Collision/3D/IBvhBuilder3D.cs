using System;

namespace Alco
{
    /// <summary>
    /// The topology an <see cref="IBvhBuilder3D"/> build produced in the tree's buffers.
    /// </summary>
    public struct BvhBuildResult3D
    {
        /// <summary>
        /// The tagged root reference: a node index (&gt;= 0), a leaf block reference (&lt; 0,
        /// encoded with <see cref="BvhNode3D.EncodeLeaf"/>), or <see cref="BvhNode3D.EmptyChild"/>
        /// for an empty tree.
        /// </summary>
        public int Root;

        /// <summary>
        /// The number of internal nodes written to the node buffer.
        /// </summary>
        public int NodeCount;

        /// <summary>
        /// The number of leaf blocks written to the leaf buffer.
        /// </summary>
        public int LeafCount;

        /// <summary>
        /// The maximum depth of the tree (a tree of a single leaf block = 1, an empty tree = 0).
        /// The tree sizes its traversal stacks to <c>TreeDepth * (<see cref="NativeBvh3D.Width"/> - 1) + Width</c>
        /// entries, so an implementation reporting a smaller depth than the real one overflows the
        /// traversal stacks.
        /// </summary>
        public int TreeDepth;
    }

    /// <summary>
    /// A build strategy for the 4-wide <see cref="NativeBvh3D"/>. Implementations arrange the
    /// colliders into a tree of <see cref="BvhNode3D"/> internal nodes and <see cref="BvhLeaf3D"/>
    /// leaf blocks, and write both directly into the buffers provided by the BVH, decoupling the
    /// tree structure from the construction algorithm.
    /// <para>Build contract:</para>
    /// <para>- Both buffers are pre-allocated and reused by the BVH; each holds at least <c>colliders.Length + 16</c> entries.</para>
    /// <para>- Every internal node uses 2 to 4 child lanes and every leaf block holds 1 to 4 colliders, so a conforming build has at most <c>colliders.Length</c> leaf blocks and fewer internal nodes than leaf blocks.</para>
    /// <para>- Child references follow the tagging of <see cref="BvhNode3D"/>; unused lanes carry <see cref="BvhNode3D.EmptyChild"/> and bounds that fail every traversal test.</para>
    /// <para>- Lane bounds must tightly bound the referenced child, so traversal pruning never skips a reachable collider.</para>
    /// <para>- Implementations must not allocate managed memory during Build; scratch memory should be held
    ///   by the implementation instance itself and reused across builds.</para>
    /// </summary>
    public interface IBvhBuilder3D
    {
        /// <summary>
        /// Builds the tree from the colliders into the pre-allocated buffers.
        /// </summary>
        /// <param name="colliders">The colliders to include, in any order the caller provides.</param>
        /// <param name="nodes">The pre-allocated internal node buffer to write the tree into.</param>
        /// <param name="leaves">The pre-allocated leaf block buffer to write the items into.</param>
        /// <returns>The topology of the built tree.</returns>
        BvhBuildResult3D Build(ReadOnlySpan<ColliderRef3D> colliders, Span<BvhNode3D> nodes, Span<BvhLeaf3D> leaves);
    }
}
