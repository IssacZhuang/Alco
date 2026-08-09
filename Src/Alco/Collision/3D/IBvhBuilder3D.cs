using System;

namespace Alco
{
    /// <summary>
    /// A build strategy for <see cref="NativeBvh3D"/>. Implementations arrange the colliders into a binary
    /// tree and write it directly into the node buffer provided by the BVH, decoupling the tree structure
    /// from the construction algorithm.
    /// <para>Build contract:</para>
    /// <para>- The node buffer is pre-allocated and reused by the BVH; its length is at least 2n + sqrt(n) + 2.</para>
    /// <para>- Leaves are written to nodes[0, n) in the builder-chosen final order, internal nodes after the leaves.</para>
    /// <para>- Child indices address the whole node buffer.</para>
    /// <para>- Implementations must not allocate managed memory during Build; scratch memory should be held
    ///   by the implementation instance itself and reused across builds.</para>
    /// </summary>
    public interface IBvhBuilder3D
    {
        /// <summary>
        /// Builds the tree from the colliders into the pre-allocated node buffer.
        /// </summary>
        /// <param name="colliders">The colliders to include, in any order the caller provides.</param>
        /// <param name="nodes">The pre-allocated node buffer to write the tree into.</param>
        /// <param name="nodeCount">Outputs the total number of nodes written.</param>
        /// <param name="root">Outputs the index of the root node, or -1 when there are no colliders.</param>
        /// <param name="treeDepth">Outputs the maximum depth of the tree (leaf level = 1, empty tree = 0).</param>
        void Build(ReadOnlySpan<ColliderRef3D> colliders, Span<BvhNode3D> nodes,
                   out int nodeCount, out int root, out int treeDepth);
    }
}
