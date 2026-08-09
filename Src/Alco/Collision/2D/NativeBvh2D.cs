using System;
using System.Numerics;
using System.Runtime.CompilerServices;


namespace Alco
{
    /// <summary>
    /// A native implementation of a Bounding Volume Hierarchy (BVH) for 2D collision detection.
    /// The tree structure only owns storage and traversal; the construction algorithm is decoupled
    /// into <see cref="IBvhBuilder2D"/> implementations that write the tree directly into the
    /// pre-allocated node buffer.
    /// </summary>
    public unsafe class NativeBvh2D : IDisposable
    {
        private NativeBuffer<BvhNode2D> _nodes;

        private int _rootIndex;
        private int _nodeSize;
        private int _treeDepth;
        private bool _isDisposed;

        /// <summary>
        /// Gets the current number of nodes in the BVH.
        /// </summary>
        public int Size => _nodeSize;

        /// <summary>
        /// Gets the maximum capacity of nodes in the BVH.
        /// </summary>
        public int Capacity => _nodes.Length;

        /// <summary>
        /// Initializes a new instance of the <see cref="NativeBvh2D"/> class.
        /// </summary>
        public NativeBvh2D()
        {
            _isDisposed = false;
        }

        /// <summary>
        /// Casts a ray against the BVH to find the closest hit.
        /// This method is thread-safe for concurrent queries, but cannot be called concurrently with <see cref="BuildTree(ReadOnlySpan{ColliderRef2D}, IBvhBuilder2D)"/>.
        /// </summary>
        /// <param name="ray">The ray to cast.</param>
        /// <returns>The result of the ray cast containing hit information.</returns>
        public RayCastResult2D CastRayClosestHit(Ray2D ray)
        {
            if (_nodeSize == 0)
            {
                return RayCastResult2D.none;
            }

            return CastRayClosestHitCore(ref ray, _rootIndex);
        }

        /// <summary>
        /// Casts a ray against the BVH and collects hits using the provided collector.
        /// This method is thread-safe for concurrent queries, but cannot be called concurrently with <see cref="BuildTree(ReadOnlySpan{ColliderRef2D}, IBvhBuilder2D)"/>.
        /// </summary>
        /// <typeparam name="TCollector">The type of the collision collector.</typeparam>
        /// <param name="ray">The ray to cast.</param>
        /// <param name="collector">The collector to gather hit results.</param>
        public void CastRay<TCollector>(Ray2D ray, ref TCollector collector) where TCollector : struct, IBvhRayCastCollector2D
        {
            if (_nodeSize == 0)
            {
                return;
            }

            CastRayCore(ref ray, _rootIndex, ref collector);
        }

        /// <summary>
        /// Casts a sphere collider against the BVH to find all overlapping colliders.
        /// This method is thread-safe for concurrent queries, but cannot be called concurrently with <see cref="BuildTree(ReadOnlySpan{ColliderRef2D}, IBvhBuilder2D)"/>.
        /// </summary>
        /// <typeparam name="TCollector">The type of the collision collector.</typeparam>
        /// <param name="shape">The sphere shape to cast.</param>
        /// <param name="collector">The collector to gather hit results.</param>
        public void CastSphere<TCollector>(in ShapeSphere2D shape, ref TCollector collector) where TCollector : struct, IBvhCollisionCastCollector2D
        {
            if (_nodeSize == 0)
            {
                return;
            }

            ColliderSphere2D collider = new ColliderSphere2D { Shape = shape };
            CastSphereCore(ref collider, _rootIndex, ref collector);
        }

        /// <summary>
        /// Casts a box collider against the BVH to find all overlapping colliders.
        /// This method is thread-safe for concurrent queries, but cannot be called concurrently with <see cref="BuildTree(ReadOnlySpan{ColliderRef2D}, IBvhBuilder2D)"/>.
        /// </summary>
        /// <typeparam name="TCollector">The type of the collision collector.</typeparam>
        /// <param name="shape">The box shape to cast.</param>
        /// <param name="collector">The collector to gather hit results.</param>
        public void CastBox<TCollector>(in ShapeBox2D shape, ref TCollector collector) where TCollector : struct, IBvhCollisionCastCollector2D
        {
            if (_nodeSize == 0)
            {
                return;
            }

            ColliderBox2D collider = new ColliderBox2D { Shape = shape };
            CastBoxCore(ref collider, _rootIndex, ref collector);
        }

        /// <summary>
        /// Casts a point against the BVH to find all colliders containing the point.
        /// This method is thread-safe for concurrent queries, but cannot be called concurrently with <see cref="BuildTree(ReadOnlySpan{ColliderRef2D}, IBvhBuilder2D)"/>.
        /// </summary>
        /// <typeparam name="TCollector">The type of the collision collector.</typeparam>
        /// <param name="point">The point to test.</param>
        /// <param name="collector">The collector to gather hit results.</param>
        public void CastPoint<TCollector>(Vector2 point, ref TCollector collector) where TCollector : struct, IBvhCollisionCastCollector2D
        {
            if (_nodeSize > 0)
            {
                CastPointCollectorCore(point, _rootIndex, ref collector);
            }
        }

        /// <summary>
        /// Builds the BVH tree from a collection of colliders using the default builder,
        /// which preserves the input order (see <see cref="PairingOrderBvhBuilder2D"/>).
        /// This method is NOT thread-safe and cannot be called concurrently with any query methods.
        /// </summary>
        /// <param name="colliders">The colliders to include in the tree.</param>
        public void BuildTree(ReadOnlySpan<ColliderRef2D> colliders)
        {
            BuildTree(colliders, PairingOrderBvhBuilder2D.Shared);
        }

        /// <summary>
        /// Builds the BVH tree from a collection of colliders using the specified build algorithm.
        /// The BVH does not interpret the collider order; the builder fully decides the final
        /// tree topology and writes it into the pre-allocated, reused node buffer.
        /// This method is NOT thread-safe and cannot be called concurrently with any query methods.
        /// </summary>
        /// <param name="colliders">The colliders to include in the tree.</param>
        /// <param name="builder">The build algorithm to use.</param>
        public void BuildTree(ReadOnlySpan<ColliderRef2D> colliders, IBvhBuilder2D builder)
        {
            _nodes.SetSizeWithoutCopy(colliders.Length * 2 + (int)math.sqrt(colliders.Length) + 2);
            builder.Build(colliders, _nodes.AsSpan(), out _nodeSize, out _rootIndex, out _treeDepth);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private BvhNode2D GetNode(int index)
        {
            return _nodes.UnsafePointer[index];
        }


        // cast collider implementation


        private RayCastResult2D CastRayClosestHitCore(ref Ray2D ray, int rootIndex)
        {
            int* stack = stackalloc int[_treeDepth];
            int stackCount = 0;
            stack[stackCount++] = rootIndex;
            RayCastResult2D result = RayCastResult2D.none;

            BoundingBox2D rayBox = ray.GetBoundingBox();

            while (stackCount > 0)
            {
                BvhNode2D top = GetNode(stack[--stackCount]);

                if (!rayBox.Intersects(top.Bounds)) continue;

                if (top.IsLeaf)
                {
                    if (top.Collider.IntersectRay(ray, out RaycastHit2D hitInfo))
                    {
                        if (!result.Hit || result.Hit && hitInfo.Fraction < result.HitInfo.Fraction)
                        {
                            result.Hit = true;
                            result.HitInfo = hitInfo;
                            result.Collider = top.Collider;
                        }
                    }

                    continue;

                }

                if (top.Left >= 0)
                {
                    stack[stackCount++] = top.Left;
                }

                if (top.Right >= 0)
                {
                    stack[stackCount++] = top.Right;
                }

            }

            return result;
        }

        private void CastRayCore<TCollector>(ref Ray2D ray, int rootIndex, ref TCollector collector) where TCollector : struct, IBvhRayCastCollector2D
        {
            int* stack = stackalloc int[_treeDepth];
            int stackCount = 0;
            stack[stackCount++] = rootIndex;

            BoundingBox2D rayBox = ray.GetBoundingBox();

            while (stackCount > 0)
            {
                BvhNode2D top = GetNode(stack[--stackCount]);

                if (!rayBox.Intersects(top.Bounds)) continue;

                if (top.IsLeaf)
                {
                    if (top.Collider.IntersectRay(ray, out RaycastHit2D hitInfo))
                    {
                        RayCastResult2D resultItem = new RayCastResult2D
                        {
                            Hit = true,
                            HitInfo = hitInfo,
                            Collider = top.Collider
                        };
                        if (!collector.OnHit(resultItem))
                        {
                            return;
                        }
                    }
                    continue;
                }

                if (top.Left >= 0)
                {
                    stack[stackCount++] = top.Left;
                }

                if (top.Right >= 0)
                {
                    stack[stackCount++] = top.Right;
                }
            }
        }

        private void CastSphereCore<TCollector>(ref ColliderSphere2D collider, int rootIndex, ref TCollector collector) where TCollector : struct, IBvhCollisionCastCollector2D
        {
            int* stack = stackalloc int[_treeDepth];
            int stackCount = 0;
            stack[stackCount++] = rootIndex;
            BoundingBox2D aabb = collider.GetBoundingBox();

            while (stackCount > 0)
            {
                BvhNode2D top = GetNode(stack[--stackCount]);

                if (!aabb.Intersects(top.Bounds)) continue;

                if (top.IsLeaf)
                {
                    if (collider.CollidesWith(top.Collider.UnsafePointer))
                    {
                        ColliderCastResult2D resultItem = new ColliderCastResult2D
                        {
                            Hit = true,
                            Collider = top.Collider
                        };
                        if (!collector.OnHit(resultItem))
                        {
                            return;
                        }
                    }
                    continue;
                }

                if (top.Left >= 0)
                {
                    stack[stackCount++] = top.Left;
                }

                if (top.Right >= 0)
                {
                    stack[stackCount++] = top.Right;
                }
            }
        }

        private void CastBoxCore<TCollector>(ref ColliderBox2D collider, int rootIndex, ref TCollector collector) where TCollector : struct, IBvhCollisionCastCollector2D
        {
            int* stack = stackalloc int[_treeDepth];
            int stackCount = 0;
            stack[stackCount++] = rootIndex;
            BoundingBox2D aabb = collider.GetBoundingBox();

            while (stackCount > 0)
            {
                BvhNode2D top = GetNode(stack[--stackCount]);

                if (!aabb.Intersects(top.Bounds)) continue;

                if (top.IsLeaf)
                {
                    if (collider.CollidesWith(top.Collider.UnsafePointer))
                    {
                        ColliderCastResult2D resultItem = new ColliderCastResult2D
                        {
                            Hit = true,
                            Collider = top.Collider
                        };
                        if (!collector.OnHit(resultItem))
                        {
                            return;
                        }
                    }
                    continue;
                }

                if (top.Left >= 0)
                {
                    stack[stackCount++] = top.Left;
                }

                if (top.Right >= 0)
                {
                    stack[stackCount++] = top.Right;
                }
            }
        }

        private void CastPointCollectorCore<TCollector>(Vector2 point, int rootIndex, ref TCollector collector) where TCollector : struct, IBvhCollisionCastCollector2D
        {
            int* stack = stackalloc int[_treeDepth];
            int stackCount = 0;
            stack[stackCount++] = rootIndex;

            while (stackCount > 0)
            {
                BvhNode2D top = GetNode(stack[--stackCount]);

                if (!top.Bounds.Contains(point)) continue;

                if (top.IsLeaf)
                {
                    if (top.Collider.IntersectPoint(point))
                    {
                        ColliderCastResult2D resultItem = new ColliderCastResult2D
                        {
                            Hit = true,
                            Collider = top.Collider
                        };
                        if (!collector.OnHit(resultItem))
                        {
                            return;
                        }
                    }
                    continue;
                }

                if (top.Left >= 0)
                {
                    stack[stackCount++] = top.Left;
                }

                if (top.Right >= 0)
                {
                    stack[stackCount++] = top.Right;
                }
            }
        }


        /// <summary>
        /// Releases all resources used by the <see cref="NativeBvh2D"/>.
        /// </summary>
        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            _nodes.Dispose();
            _isDisposed = true;
        }
    }
}
