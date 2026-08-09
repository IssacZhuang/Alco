using System;

namespace Alco
{
    /// <summary>
    /// The default <see cref="IBvhBuilder2D"/> that preserves the historical build behavior of
    /// <see cref="NativeBvh2D"/>: leaves keep their input order and are paired bottom-up into a
    /// balanced binary tree. It performs no spatial clustering, so tree quality depends entirely
    /// on the input order of the colliders. Kept as the baseline for build algorithm comparisons.
    /// </summary>
    public class PairingOrderBvhBuilder2D : IBvhBuilder2D
    {
        /// <summary>
        /// A shared instance of the stateless pairing builder.
        /// </summary>
        public static readonly PairingOrderBvhBuilder2D Shared = new PairingOrderBvhBuilder2D();

        /// <inheritdoc/>
        public void Build(ReadOnlySpan<ColliderRef2D> colliders, Span<BvhNode2D> nodes,
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

            // leaves in input order
            for (int i = 0; i < n; i++)
            {
                ColliderRef2D collider = colliders[i];
                nodes[i] = new BvhNode2D
                {
                    Left = -1,
                    Right = -1,
                    Collider = collider,
                    Bounds = collider.GetBoundingBox(),
                };
            }

            if (n == 1)
            {
                nodeCount = 1;
                root = 0;
                treeDepth = 1;
                return;
            }

            // pair adjacent nodes bottom-up, level by level
            int start = 0;
            int end = n;
            int depth = 1; // leaf level
            nodeCount = n;

            while (start < end - 2)
            {
                int parentCount = (end - start + 1) / 2;
                for (int i = 0; i < parentCount; i++)
                {
                    int left = start + i * 2;
                    int right = start + i * 2 + 1;
                    nodes[end + i] = right >= end
                        ? CreateParent(nodes, left)
                        : CreateParent(nodes, left, right);
                }

                start = end;
                end = start + parentCount;
                nodeCount += parentCount;
                depth++;
            }

            if (end - start == 2)
            {
                nodes[end] = CreateParent(nodes, start, start + 1);
                root = end;
                nodeCount++;
                depth++;
            }
            else
            {
                root = start; // a single node remains
            }

            treeDepth = depth;
        }

        private static BvhNode2D CreateParent(Span<BvhNode2D> nodes, int singleChild)
        {
            return new BvhNode2D
            {
                Left = singleChild,
                Right = -1,
                Bounds = nodes[singleChild].Bounds
            };
        }

        private static BvhNode2D CreateParent(Span<BvhNode2D> nodes, int left, int right)
        {
            return new BvhNode2D
            {
                Left = left,
                Right = right,
                Bounds = BoundingBox2D.Merge(nodes[left].Bounds, nodes[right].Bounds)
            };
        }
    }
}
