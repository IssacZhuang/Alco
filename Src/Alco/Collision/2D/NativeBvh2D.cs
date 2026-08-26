using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;

namespace Alco
{
    /// <summary>
    /// A native implementation of a 4-wide Bounding Volume Hierarchy (BVH) for 2D collision detection.
    /// Internal nodes store the bounds of up to four children structure-of-arrays style, so every
    /// traversal step culls all children with a single <see cref="Vector128{T}"/> comparison per
    /// axis, and leaves are 4-item blocks that run the same SoA AABB cull before the exact shape
    /// tests (<c>IntersectRay</c> / <c>CollidesWith</c> / <c>IntersectPoint</c>) on the surviving
    /// slots only.
    /// The tree only owns storage and traversal; the construction algorithm is decoupled into
    /// <see cref="IBvhBuilder2D"/> implementations that write the tree directly into the
    /// pre-allocated buffers. The default build uses <see cref="MortonBvhBuilder2D"/>.
    /// </summary>
    public unsafe class NativeBvh2D : IDisposable
    {
        /// <summary>
        /// The number of children per internal node and colliders per leaf block.
        /// </summary>
        public const int Width = 4;

        /// <summary>
        /// The maximum number of colliders per leaf block.
        /// </summary>
        public const int MaxLeafItems = 4;

        private NativeBuffer<BvhNode2D> _nodes;
        private NativeBuffer<BvhLeaf2D> _leaves;
        private MortonBvhBuilder2D? _defaultBuilder;

        private int _root = BvhNode2D.EmptyChild;
        private int _nodeCount;
        private int _leafCount;
        private int _treeDepth;
        private bool _isDisposed;

        /// <summary>
        /// Gets the current number of internal nodes in the tree.
        /// </summary>
        public int NodeCount => _nodeCount;

        /// <summary>
        /// Gets the current number of leaf blocks in the tree.
        /// </summary>
        public int LeafCount => _leafCount;

        /// <summary>
        /// Gets the maximum depth of the tree (a single leaf block = 1, an empty tree = 0).
        /// </summary>
        public int TreeDepth => _treeDepth;

        /// <summary>
        /// Initializes a new instance of the <see cref="NativeBvh2D"/> class.
        /// </summary>
        public NativeBvh2D()
        {
            _isDisposed = false;
        }

        /// <summary>
        /// Casts a ray against the BVH to find the closest hit.
        /// Traversal is near-first with distance-stack pruning: the AABB entry fraction of a node
        /// bounds any exact shape hit fraction inside it from below, so subtrees that cannot beat
        /// the running best fraction are skipped.
        /// This method is thread-safe for concurrent queries, but cannot be called concurrently with <see cref="BuildTree(ReadOnlySpan{ColliderRef2D}, IBvhBuilder2D)"/>.
        /// </summary>
        /// <param name="ray">The ray to cast.</param>
        /// <returns>The result of the ray cast containing hit information.</returns>
        public RayCastResult2D CastRayClosestHit(Ray2D ray)
        {
            if (_root == BvhNode2D.EmptyChild)
            {
                return RayCastResult2D.none;
            }

            Vector2 origin = ray.Origin;
            Vector2 displacement = ray.Displacement;
            float invX = displacement.X != 0f ? 1f / displacement.X : float.MaxValue;
            float invY = displacement.Y != 0f ? 1f / displacement.Y : float.MaxValue;
            bool posX = invX >= 0f;
            bool posY = invY >= 0f;
            Vector128<float> orgX = Vector128.Create(origin.X);
            Vector128<float> orgY = Vector128.Create(origin.Y);
            Vector128<float> rdirX = Vector128.Create(invX);
            Vector128<float> rdirY = Vector128.Create(invY);
            Vector128<float> zero = Vector128<float>.Zero;
            Vector128<float> one = Vector128.Create(1f);

            BvhNode2D* nodes = _nodes.UnsafePointer;
            BvhLeaf2D* leaves = _leaves.UnsafePointer;

            float bestT = 1f;
            RayCastResult2D result = RayCastResult2D.none;

            int* stackChild = stackalloc int[_treeDepth * (Width - 1) + Width];
            float* stackDist = stackalloc float[_treeDepth * (Width - 1) + Width];
            int* orderChild = stackalloc int[Width];
            float* orderDist = stackalloc float[Width];
            int sp = 0;

            int cur = _root;
            float curDist = 0f;

            while (true)
            {
                while (true)
                {
                    if (curDist > bestT)
                    {
                        break; // stale entry: a nearer hit was found since it was pushed
                    }

                    if (cur < 0)
                    {
                        // leaf block: cheap SoA AABB cull, then the exact shape test
                        BvhLeaf2D* leaf = leaves + BvhNode2D.DecodeLeaf(cur);
                        Vector128<float> t0 = Vector128.Multiply(Vector128.Subtract(leaf->LowerX, orgX), rdirX);
                        Vector128<float> t1 = Vector128.Multiply(Vector128.Subtract(leaf->UpperX, orgX), rdirX);
                        Vector128<float> tMin = posX ? t0 : t1;
                        Vector128<float> tMax = posX ? t1 : t0;
                        t0 = Vector128.Multiply(Vector128.Subtract(leaf->LowerY, orgY), rdirY);
                        t1 = Vector128.Multiply(Vector128.Subtract(leaf->UpperY, orgY), rdirY);
                        tMin = Vector128.Max(tMin, posY ? t0 : t1);
                        tMax = Vector128.Min(tMax, posY ? t1 : t0);
                        // segment acceptance is independent of the running best (an origin inside
                        // a box yields negative entries); the best fraction only ranks the entry
                        Vector128<float> leafSeg = Vector128.LessThanOrEqual(Vector128.Max(tMin, zero), Vector128.Min(tMax, one));
                        int leafMask = (int)Vector128.BitwiseAnd(leafSeg, Vector128.LessThanOrEqual(tMin, Vector128.Create(bestT))).ExtractMostSignificantBits();
                        if (leafMask != 0)
                        {
                            if ((leafMask & 1) != 0 && leaf->C0.HasCollider && leaf->C0.IntersectRay(ray, out RaycastHit2D hit0) && hit0.Fraction < bestT)
                            {
                                bestT = hit0.Fraction;
                                result = new RayCastResult2D { Hit = true, HitInfo = hit0, Collider = leaf->C0 };
                            }
                            if ((leafMask & 2) != 0 && leaf->C1.HasCollider && leaf->C1.IntersectRay(ray, out RaycastHit2D hit1) && hit1.Fraction < bestT)
                            {
                                bestT = hit1.Fraction;
                                result = new RayCastResult2D { Hit = true, HitInfo = hit1, Collider = leaf->C1 };
                            }
                            if ((leafMask & 4) != 0 && leaf->C2.HasCollider && leaf->C2.IntersectRay(ray, out RaycastHit2D hit2) && hit2.Fraction < bestT)
                            {
                                bestT = hit2.Fraction;
                                result = new RayCastResult2D { Hit = true, HitInfo = hit2, Collider = leaf->C2 };
                            }
                            if ((leafMask & 8) != 0 && leaf->C3.HasCollider && leaf->C3.IntersectRay(ray, out RaycastHit2D hit3) && hit3.Fraction < bestT)
                            {
                                bestT = hit3.Fraction;
                                result = new RayCastResult2D { Hit = true, HitInfo = hit3, Collider = leaf->C3 };
                            }
                        }
                        break;
                    }

                    BvhNode2D* node = nodes + cur;
                    Vector128<float> s0 = Vector128.Multiply(Vector128.Subtract(node->LowerX, orgX), rdirX);
                    Vector128<float> s1 = Vector128.Multiply(Vector128.Subtract(node->UpperX, orgX), rdirX);
                    Vector128<float> sMin = posX ? s0 : s1;
                    Vector128<float> sMax = posX ? s1 : s0;
                    s0 = Vector128.Multiply(Vector128.Subtract(node->LowerY, orgY), rdirY);
                    s1 = Vector128.Multiply(Vector128.Subtract(node->UpperY, orgY), rdirY);
                    sMin = Vector128.Max(sMin, posY ? s0 : s1);
                    sMax = Vector128.Min(sMax, posY ? s1 : s0);
                    Vector128<float> nodeSeg = Vector128.LessThanOrEqual(Vector128.Max(sMin, zero), Vector128.Min(sMax, one));
                    int nodeMask = (int)Vector128.BitwiseAnd(nodeSeg, Vector128.LessThanOrEqual(sMin, Vector128.Create(bestT))).ExtractMostSignificantBits();
                    if (nodeMask == 0)
                    {
                        break;
                    }

                    int count = 0;
                    for (int i = 0; i < Width; i++)
                    {
                        if ((nodeMask & (1 << i)) != 0)
                        {
                            int child = node->Children[i];
                            float dist = sMin.GetElement(i);
                            int j = count++;
                            while (j > 0 && orderDist[j - 1] > dist)
                            {
                                orderDist[j] = orderDist[j - 1];
                                orderChild[j] = orderChild[j - 1];
                                j--;
                            }
                            orderDist[j] = dist;
                            orderChild[j] = child;
                        }
                    }

                    cur = orderChild[0];
                    curDist = orderDist[0];
                    for (int k = count - 1; k >= 1; k--)
                    {
                        stackChild[sp] = orderChild[k];
                        stackDist[sp] = orderDist[k];
                        sp++;
                    }
                }

                if (sp == 0)
                {
                    break;
                }
                sp--;
                cur = stackChild[sp];
                curDist = stackDist[sp];
            }

            return result;
        }

        /// <summary>
        /// Casts a ray against the BVH and collects hits using the provided collector; the
        /// collector stops the traversal by returning false.
        /// This method is thread-safe for concurrent queries, but cannot be called concurrently with <see cref="BuildTree(ReadOnlySpan{ColliderRef2D}, IBvhBuilder2D)"/>.
        /// </summary>
        /// <typeparam name="TCollector">The type of the collision collector.</typeparam>
        /// <param name="ray">The ray to cast.</param>
        /// <param name="collector">The collector to gather hit results.</param>
        public void CastRay<TCollector>(Ray2D ray, ref TCollector collector) where TCollector : struct, IBvhRayCastCollector2D
        {
            if (_root == BvhNode2D.EmptyChild)
            {
                return;
            }

            Vector2 origin = ray.Origin;
            Vector2 displacement = ray.Displacement;
            float invX = displacement.X != 0f ? 1f / displacement.X : float.MaxValue;
            float invY = displacement.Y != 0f ? 1f / displacement.Y : float.MaxValue;
            bool posX = invX >= 0f;
            bool posY = invY >= 0f;
            Vector128<float> orgX = Vector128.Create(origin.X);
            Vector128<float> orgY = Vector128.Create(origin.Y);
            Vector128<float> rdirX = Vector128.Create(invX);
            Vector128<float> rdirY = Vector128.Create(invY);
            Vector128<float> zero = Vector128<float>.Zero;
            Vector128<float> one = Vector128.Create(1f);

            BvhNode2D* nodes = _nodes.UnsafePointer;
            BvhLeaf2D* leaves = _leaves.UnsafePointer;

            int* stack = stackalloc int[_treeDepth * (Width - 1) + Width];
            int sp = 0;
            int cur = _root;

            while (true)
            {
                while (true)
                {
                    if (cur < 0)
                    {
                        BvhLeaf2D* leaf = leaves + BvhNode2D.DecodeLeaf(cur);
                        Vector128<float> t0 = Vector128.Multiply(Vector128.Subtract(leaf->LowerX, orgX), rdirX);
                        Vector128<float> t1 = Vector128.Multiply(Vector128.Subtract(leaf->UpperX, orgX), rdirX);
                        Vector128<float> tMin = posX ? t0 : t1;
                        Vector128<float> tMax = posX ? t1 : t0;
                        t0 = Vector128.Multiply(Vector128.Subtract(leaf->LowerY, orgY), rdirY);
                        t1 = Vector128.Multiply(Vector128.Subtract(leaf->UpperY, orgY), rdirY);
                        tMin = Vector128.Max(tMin, posY ? t0 : t1);
                        tMax = Vector128.Min(tMax, posY ? t1 : t0);
                        int leafMask = (int)Vector128.LessThanOrEqual(Vector128.Max(tMin, zero), Vector128.Min(tMax, one)).ExtractMostSignificantBits();
                        if ((leafMask & 1) != 0 && leaf->C0.HasCollider && leaf->C0.IntersectRay(ray, out RaycastHit2D hit0))
                        {
                            if (!collector.OnHit(new RayCastResult2D { Hit = true, HitInfo = hit0, Collider = leaf->C0 })) return;
                        }
                        if ((leafMask & 2) != 0 && leaf->C1.HasCollider && leaf->C1.IntersectRay(ray, out RaycastHit2D hit1))
                        {
                            if (!collector.OnHit(new RayCastResult2D { Hit = true, HitInfo = hit1, Collider = leaf->C1 })) return;
                        }
                        if ((leafMask & 4) != 0 && leaf->C2.HasCollider && leaf->C2.IntersectRay(ray, out RaycastHit2D hit2))
                        {
                            if (!collector.OnHit(new RayCastResult2D { Hit = true, HitInfo = hit2, Collider = leaf->C2 })) return;
                        }
                        if ((leafMask & 8) != 0 && leaf->C3.HasCollider && leaf->C3.IntersectRay(ray, out RaycastHit2D hit3))
                        {
                            if (!collector.OnHit(new RayCastResult2D { Hit = true, HitInfo = hit3, Collider = leaf->C3 })) return;
                        }
                        break;
                    }

                    BvhNode2D* node = nodes + cur;
                    Vector128<float> s0 = Vector128.Multiply(Vector128.Subtract(node->LowerX, orgX), rdirX);
                    Vector128<float> s1 = Vector128.Multiply(Vector128.Subtract(node->UpperX, orgX), rdirX);
                    Vector128<float> sMin = posX ? s0 : s1;
                    Vector128<float> sMax = posX ? s1 : s0;
                    s0 = Vector128.Multiply(Vector128.Subtract(node->LowerY, orgY), rdirY);
                    s1 = Vector128.Multiply(Vector128.Subtract(node->UpperY, orgY), rdirY);
                    sMin = Vector128.Max(sMin, posY ? s0 : s1);
                    sMax = Vector128.Min(sMax, posY ? s1 : s0);
                    int nodeMask = (int)Vector128.LessThanOrEqual(Vector128.Max(sMin, zero), Vector128.Min(sMax, one)).ExtractMostSignificantBits();
                    if (nodeMask == 0)
                    {
                        break;
                    }

                    for (int i = Width - 1; i >= 0; i--)
                    {
                        if ((nodeMask & (1 << i)) != 0)
                        {
                            stack[sp++] = node->Children[i];
                        }
                    }
                    break;
                }

                if (sp == 0)
                {
                    break;
                }
                cur = stack[--sp];
            }
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
            ColliderSphere2D collider = new ColliderSphere2D { Shape = shape };
            CastOverlapCore(ref collider, collider.GetBoundingBox(), ref collector);
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
            ColliderBox2D collider = new ColliderBox2D { Shape = shape };
            CastOverlapCore(ref collider, collider.GetBoundingBox(), ref collector);
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
            if (_root == BvhNode2D.EmptyChild)
            {
                return;
            }

            Vector128<float> px = Vector128.Create(point.X);
            Vector128<float> py = Vector128.Create(point.Y);

            BvhNode2D* nodes = _nodes.UnsafePointer;
            BvhLeaf2D* leaves = _leaves.UnsafePointer;

            int* stack = stackalloc int[_treeDepth * (Width - 1) + Width];
            int sp = 0;
            int cur = _root;

            while (true)
            {
                while (true)
                {
                    if (cur < 0)
                    {
                        BvhLeaf2D* leaf = leaves + BvhNode2D.DecodeLeaf(cur);
                        Vector128<float> leafValid = Vector128.BitwiseAnd(
                            Vector128.GreaterThanOrEqual(leaf->UpperX, px),
                            Vector128.LessThanOrEqual(leaf->LowerX, px));
                        leafValid = Vector128.BitwiseAnd(leafValid, Vector128.BitwiseAnd(
                            Vector128.GreaterThanOrEqual(leaf->UpperY, py),
                            Vector128.LessThanOrEqual(leaf->LowerY, py)));
                        int leafMask = (int)leafValid.ExtractMostSignificantBits();
                        if ((leafMask & 1) != 0 && leaf->C0.HasCollider && leaf->C0.IntersectPoint(point))
                        {
                            if (!collector.OnHit(new ColliderCastResult2D { Hit = true, Collider = leaf->C0 })) return;
                        }
                        if ((leafMask & 2) != 0 && leaf->C1.HasCollider && leaf->C1.IntersectPoint(point))
                        {
                            if (!collector.OnHit(new ColliderCastResult2D { Hit = true, Collider = leaf->C1 })) return;
                        }
                        if ((leafMask & 4) != 0 && leaf->C2.HasCollider && leaf->C2.IntersectPoint(point))
                        {
                            if (!collector.OnHit(new ColliderCastResult2D { Hit = true, Collider = leaf->C2 })) return;
                        }
                        if ((leafMask & 8) != 0 && leaf->C3.HasCollider && leaf->C3.IntersectPoint(point))
                        {
                            if (!collector.OnHit(new ColliderCastResult2D { Hit = true, Collider = leaf->C3 })) return;
                        }
                        break;
                    }

                    BvhNode2D* node = nodes + cur;
                    Vector128<float> nodeValid = Vector128.BitwiseAnd(
                        Vector128.GreaterThanOrEqual(node->UpperX, px),
                        Vector128.LessThanOrEqual(node->LowerX, px));
                    nodeValid = Vector128.BitwiseAnd(nodeValid, Vector128.BitwiseAnd(
                        Vector128.GreaterThanOrEqual(node->UpperY, py),
                        Vector128.LessThanOrEqual(node->LowerY, py)));
                    int nodeMask = (int)nodeValid.ExtractMostSignificantBits();
                    if (nodeMask == 0)
                    {
                        break;
                    }

                    for (int i = Width - 1; i >= 0; i--)
                    {
                        if ((nodeMask & (1 << i)) != 0)
                        {
                            stack[sp++] = node->Children[i];
                        }
                    }
                    break;
                }

                if (sp == 0)
                {
                    break;
                }
                cur = stack[--sp];
            }
        }

        /// <summary>
        /// Builds the BVH tree from a collection of colliders using the default
        /// <see cref="MortonBvhBuilder2D"/> owned by this tree.
        /// This method is NOT thread-safe and cannot be called concurrently with any query methods.
        /// </summary>
        /// <param name="colliders">The colliders to include in the tree.</param>
        public void BuildTree(ReadOnlySpan<ColliderRef2D> colliders)
        {
            _defaultBuilder ??= new MortonBvhBuilder2D();
            BuildTree(colliders, _defaultBuilder);
        }

        /// <summary>
        /// Builds the BVH tree from a collection of colliders using the specified build algorithm.
        /// The BVH does not interpret the collider order; the builder fully decides the final
        /// tree topology and writes it into the pre-allocated, reused buffers.
        /// This method is NOT thread-safe and cannot be called concurrently with any query methods.
        /// </summary>
        /// <param name="colliders">The colliders to include in the tree.</param>
        /// <param name="builder">The build algorithm to use.</param>
        public void BuildTree(ReadOnlySpan<ColliderRef2D> colliders, IBvhBuilder2D builder)
        {
            _nodes.SetSizeWithoutCopy(colliders.Length + 16);
            _leaves.SetSizeWithoutCopy(colliders.Length + 16);
            BvhBuildResult2D result = builder.Build(colliders, _nodes.AsSpan(), _leaves.AsSpan());
            _root = result.Root;
            _nodeCount = result.NodeCount;
            _leafCount = result.LeafCount;
            _treeDepth = result.TreeDepth;
        }

        private void CastOverlapCore<TCollider, TCollector>(ref TCollider castCollider, BoundingBox2D aabb, ref TCollector collector)
            where TCollider : unmanaged, ICollider2D
            where TCollector : struct, IBvhCollisionCastCollector2D
        {
            if (_root == BvhNode2D.EmptyChild)
            {
                return;
            }

            Vector128<float> qMinX = Vector128.Create(aabb.Min.X);
            Vector128<float> qMinY = Vector128.Create(aabb.Min.Y);
            Vector128<float> qMaxX = Vector128.Create(aabb.Max.X);
            Vector128<float> qMaxY = Vector128.Create(aabb.Max.Y);

            BvhNode2D* nodes = _nodes.UnsafePointer;
            BvhLeaf2D* leaves = _leaves.UnsafePointer;

            int* stack = stackalloc int[_treeDepth * (Width - 1) + Width];
            int sp = 0;
            int cur = _root;

            while (true)
            {
                while (true)
                {
                    if (cur < 0)
                    {
                        BvhLeaf2D* leaf = leaves + BvhNode2D.DecodeLeaf(cur);
                        Vector128<float> leafValid = Vector128.BitwiseAnd(
                            Vector128.GreaterThanOrEqual(leaf->UpperX, qMinX),
                            Vector128.LessThanOrEqual(leaf->LowerX, qMaxX));
                        leafValid = Vector128.BitwiseAnd(leafValid, Vector128.BitwiseAnd(
                            Vector128.GreaterThanOrEqual(leaf->UpperY, qMinY),
                            Vector128.LessThanOrEqual(leaf->LowerY, qMaxY)));
                        int leafMask = (int)leafValid.ExtractMostSignificantBits();
                        if ((leafMask & 1) != 0 && leaf->C0.HasCollider && castCollider.CollidesWith(leaf->C0.UnsafePointer))
                        {
                            if (!collector.OnHit(new ColliderCastResult2D { Hit = true, Collider = leaf->C0 })) return;
                        }
                        if ((leafMask & 2) != 0 && leaf->C1.HasCollider && castCollider.CollidesWith(leaf->C1.UnsafePointer))
                        {
                            if (!collector.OnHit(new ColliderCastResult2D { Hit = true, Collider = leaf->C1 })) return;
                        }
                        if ((leafMask & 4) != 0 && leaf->C2.HasCollider && castCollider.CollidesWith(leaf->C2.UnsafePointer))
                        {
                            if (!collector.OnHit(new ColliderCastResult2D { Hit = true, Collider = leaf->C2 })) return;
                        }
                        if ((leafMask & 8) != 0 && leaf->C3.HasCollider && castCollider.CollidesWith(leaf->C3.UnsafePointer))
                        {
                            if (!collector.OnHit(new ColliderCastResult2D { Hit = true, Collider = leaf->C3 })) return;
                        }
                        break;
                    }

                    BvhNode2D* node = nodes + cur;
                    Vector128<float> nodeValid = Vector128.BitwiseAnd(
                        Vector128.GreaterThanOrEqual(node->UpperX, qMinX),
                        Vector128.LessThanOrEqual(node->LowerX, qMaxX));
                    nodeValid = Vector128.BitwiseAnd(nodeValid, Vector128.BitwiseAnd(
                        Vector128.GreaterThanOrEqual(node->UpperY, qMinY),
                        Vector128.LessThanOrEqual(node->LowerY, qMaxY)));
                    int nodeMask = (int)nodeValid.ExtractMostSignificantBits();
                    if (nodeMask == 0)
                    {
                        break;
                    }

                    for (int i = Width - 1; i >= 0; i--)
                    {
                        if ((nodeMask & (1 << i)) != 0)
                        {
                            stack[sp++] = node->Children[i];
                        }
                    }
                    break;
                }

                if (sp == 0)
                {
                    break;
                }
                cur = stack[--sp];
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
            _leaves.Dispose();
            _defaultBuilder?.Dispose();
            _isDisposed = true;
        }
    }
}
