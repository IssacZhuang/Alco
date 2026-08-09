using System;

namespace Alco
{
    /// <summary>
    /// The default <see cref="IBvhBuilder3D"/> that preserves the historical build behavior of
    /// <see cref="NativeBvh3D"/>: leaves keep their input order and are paired bottom-up into a
    /// balanced binary tree. It performs no spatial clustering, so tree quality depends entirely
    /// on the input order of the colliders. Kept as the baseline for build algorithm comparisons.
    /// </summary>
    public class PairingOrderBvhBuilder3D : IBvhBuilder3D
    {
        /// <summary>
        /// A shared instance of the stateless pairing builder.
        /// </summary>
        public static readonly PairingOrderBvhBuilder3D Shared = new PairingOrderBvhBuilder3D();

        /// <inheritdoc/>
        public void Build(ReadOnlySpan<ColliderRef3D> colliders, Span<BvhNode3D> nodes,
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
                ColliderRef3D collider = colliders[i];
                nodes[i] = new BvhNode3D
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

        private static BvhNode3D CreateParent(Span<BvhNode3D> nodes, int singleChild)
        {
            return new BvhNode3D
            {
                Left = singleChild,
                Right = -1,
                Bounds = nodes[singleChild].Bounds
            };
        }

        private static BvhNode3D CreateParent(Span<BvhNode3D> nodes, int left, int right)
        {
            return new BvhNode3D
            {
                Left = left,
                Right = right,
                Bounds = BoundingBox3D.Merge(nodes[left].Bounds, nodes[right].Bounds)
            };
        }
    }
}
