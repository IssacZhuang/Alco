using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;


namespace Alco
{
    /// <summary>
    /// A native implementation of a Bounding Volume Hierarchy (BVH) for 2D collision detection.
    /// </summary>
    public unsafe class NativeBvh2D : IDisposable
    {
        private const int BatchSize = 16;

        private const int ChildCount = 2;

        /// <summary>
        /// Represents a node in the BVH tree.
        /// </summary>
        private struct Node
        {
            /// <summary>
            /// The index of the left child node, or -1 if none.
            /// </summary>
            public int left;

            /// <summary>
            /// The index of the right child node, or -1 if none.
            /// </summary>
            public int right;

            /// <summary>
            /// The bounding box of this node.
            /// </summary>
            public BoundingBox2D boundingBox;

            /// <summary>
            /// The collider associated with this node if it is a leaf.
            /// </summary>
            public ColliderRef2D collider;

            /// <summary>
            /// Gets a value indicating whether this node is a leaf node.
            /// </summary>
            public bool IsLeaf => collider.HasCollider;
        }


        private NativeBuffer<Node> _nodes;


        private Node _root;
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
        /// This method is thread-safe for concurrent queries, but cannot be called concurrently with <see cref="BuildTree"/>.
        /// </summary>
        /// <param name="ray">The ray to cast.</param>
        /// <returns>The result of the ray cast containing hit information.</returns>
        public RayCastResult2D CastRay(Ray2D ray)
        {
            if (_nodeSize == 0)
            {
                return RayCastResult2D.none;
            }

            return CastRayCore(ref ray, _root);
        }

        /// <summary>
        /// Casts a ray against the BVH to find the first hit encountered.
        /// This method is thread-safe for concurrent queries, but cannot be called concurrently with <see cref="BuildTree"/>.
        /// </summary>
        /// <param name="ray">The ray to cast.</param>
        /// <returns>The result of the ray cast containing hit information.</returns>
        public RayCastResult2D CastRayFirstHit(Ray2D ray)
        {
            if (_nodeSize == 0)
            {
                return RayCastResult2D.none;
            }

            return CastRayFirstHitCore(ref ray, _root);
        }

        /// <summary>
        /// Casts a sphere collider against the BVH to find all overlapping colliders.
        /// This method is thread-safe for concurrent queries, but cannot be called concurrently with <see cref="BuildTree"/>.
        /// </summary>
        /// <typeparam name="TCollector">The type of the collision collector.</typeparam>
        /// <param name="shape">The sphere shape to cast.</param>
        /// <param name="collector">The collector to gather hit results.</param>
        public void CastSphere<TCollector>(in ShapeSphere2D shape, ref TCollector collector) where TCollector : struct, IBvhCollisionCollector
        {
            if (_nodeSize == 0)
            {
                return;
            }

            ColliderSphere2D collider = new ColliderSphere2D { Shape = shape };
            CastSphereCore(ref collider, _root, ref collector);
        }

        /// <summary>
        /// Casts a box collider against the BVH to find all overlapping colliders.
        /// This method is thread-safe for concurrent queries, but cannot be called concurrently with <see cref="BuildTree"/>.
        /// </summary>
        /// <typeparam name="TCollector">The type of the collision collector.</typeparam>
        /// <param name="shape">The box shape to cast.</param>
        /// <param name="collector">The collector to gather hit results.</param>
        public void CastBox<TCollector>(in ShapeBox2D shape, ref TCollector collector) where TCollector : struct, IBvhCollisionCollector
        {
            if (_nodeSize == 0)
            {
                return;
            }

            ColliderBox2D collider = new ColliderBox2D { Shape = shape };
            CastBoxCore(ref collider, _root, ref collector);
        }

        /// <summary>
        /// Casts a point against the BVH to find all colliders containing the point.
        /// This method is thread-safe for concurrent queries, but cannot be called concurrently with <see cref="BuildTree"/>.
        /// </summary>
        /// <typeparam name="TCollector">The type of the collision collector.</typeparam>
        /// <param name="point">The point to test.</param>
        /// <param name="collector">The collector to gather hit results.</param>
        public void CastPoint<TCollector>(Vector2 point, ref TCollector collector) where TCollector : struct, IBvhCollisionCollector
        {
            if (_nodeSize > 0)
            {
                CastPointCollectorCore(point, _root, ref collector);
            }
        }

        /// <summary>
        /// Builds the BVH tree from a collection of colliders.
        /// This method is NOT thread-safe and cannot be called concurrently with any query methods.
        /// </summary>
        /// <param name="colliders">The colliders to include in the tree.</param>
        public void BuildTree(ReadOnlySpan<ColliderRef2D> colliders)
        {
            _nodes.SetSizeWithoutCopy(colliders.Length * 2 + (int)math.sqrt(colliders.Length) + 2);
            BuildBottomTop(colliders);
            _treeDepth = (int)math.log2(colliders.Length + 1) + 1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Node GetNode(int index)
        {
            return _nodes.UnsafePointer[index];
        }



        // cast collider implementation


        private RayCastResult2D CastRayCore(ref Ray2D ray, Node node)
        {
            //NativeStack<Node> stack = new NativeStack<Node>(_nodeSize * 2);
            Node* stack = stackalloc Node[_treeDepth];
            int stackCount = 0;
            stack[stackCount++] = node;
            RayCastResult2D result = RayCastResult2D.none;

            BoundingBox2D rayBox = ray.GetBoundingBox();

            while (stackCount > 0)
            {
                //Node top = stack.Pop();
                Node top = stack[--stackCount];

                //if (!CollisionUtility2D.RayAABB(ray, top.boundingBox)) continue;
                if (!rayBox.Intersects(top.boundingBox)) continue;

                if (top.IsLeaf)
                {
                    if (top.collider.IntersectRay(ray, out RaycastHit2D hitInfo))
                    {
                        if (!result.Hit || result.Hit && hitInfo.Fraction < result.HitInfo.Fraction)
                        {
                            result.Hit = true;
                            result.HitInfo = hitInfo;
                            result.Collider = top.collider;
                        }
                    }

                    continue;

                }

                if (top.left >= 0)
                {
                    stack[stackCount++] = GetNode(top.left);
                }

                if (top.right >= 0)
                {
                    stack[stackCount++] = GetNode(top.right);
                }

            }

            return result;
        }

        private RayCastResult2D CastRayFirstHitCore(ref Ray2D ray, Node node)
        {
            //NativeStack<Node> stack = new NativeStack<Node>(_nodeSize * 2);
            Node* stack = stackalloc Node[_treeDepth];
            int stackCount = 0;
            stack[stackCount++] = node;
            RayCastResult2D result = RayCastResult2D.none;
            BoundingBox2D rayBox = ray.GetBoundingBox();

            while (stackCount > 0)
            {
                //Node top = stack.Pop();
                Node top = stack[--stackCount];

                //if (!CollisionUtility2D.RayAABB(ray, top.boundingBox)) continue;
                if (!rayBox.Intersects(top.boundingBox)) continue;

                if (top.IsLeaf)
                {
                    if (top.collider.IntersectRay(ray, out RaycastHit2D hitInfo))
                    {
                        result.Hit = true;
                        result.HitInfo = hitInfo;
                        result.Collider = top.collider;
                        return result;
                    }

                    continue;

                }

                if (top.left >= 0)
                {
                    stack[stackCount++] = GetNode(top.left);
                }

                if (top.right >= 0)
                {
                    stack[stackCount++] = GetNode(top.right);
                }

            }

            return result;
        }

        private void CastSphereCore<TCollector>(ref ColliderSphere2D collider, Node node, ref TCollector collector) where TCollector : struct, IBvhCollisionCollector
        {
            Node* stack = stackalloc Node[_treeDepth];
            int stackCount = 0;
            stack[stackCount++] = node;
            BoundingBox2D aabb = collider.GetBoundingBox();

            while (stackCount > 0)
            {
                Node top = stack[--stackCount];

                if (!aabb.Intersects(top.boundingBox)) continue;

                if (top.IsLeaf)
                {
                    if (collider.CollidesWith(top.collider.UnsafePointer))
                    {
                        ColliderCastResult2D resultItem = new ColliderCastResult2D
                        {
                            Hit = true,
                            Collider = top.collider
                        };
                        if (!collector.OnHit(resultItem))
                        {
                            return;
                        }
                    }
                    continue;
                }

                if (top.left >= 0)
                {
                    stack[stackCount++] = GetNode(top.left);
                }

                if (top.right >= 0)
                {
                    stack[stackCount++] = GetNode(top.right);
                }
            }
        }

        private void CastBoxCore<TCollector>(ref ColliderBox2D collider, Node node, ref TCollector collector) where TCollector : struct, IBvhCollisionCollector
        {
            Node* stack = stackalloc Node[_treeDepth];
            int stackCount = 0;
            stack[stackCount++] = node;
            BoundingBox2D aabb = collider.GetBoundingBox();

            while (stackCount > 0)
            {
                Node top = stack[--stackCount];

                if (!aabb.Intersects(top.boundingBox)) continue;

                if (top.IsLeaf)
                {
                    if (collider.CollidesWith(top.collider.UnsafePointer))
                    {
                        ColliderCastResult2D resultItem = new ColliderCastResult2D
                        {
                            Hit = true,
                            Collider = top.collider
                        };
                        if (!collector.OnHit(resultItem))
                        {
                            return;
                        }
                    }
                    continue;
                }

                if (top.left >= 0)
                {
                    stack[stackCount++] = GetNode(top.left);
                }

                if (top.right >= 0)
                {
                    stack[stackCount++] = GetNode(top.right);
                }
            }
        }

        private void CastPointCollectorCore<TCollector>(Vector2 point, Node node, ref TCollector collector) where TCollector : struct, IBvhCollisionCollector
        {
            Node* stack = stackalloc Node[_treeDepth];
            int stackCount = 0;
            stack[stackCount++] = node;

            while (stackCount > 0)
            {
                Node top = stack[--stackCount];

                if (!top.boundingBox.Contains(point)) continue;

                if (top.IsLeaf)
                {
                    if (top.collider.IntersectPoint(point))
                    {
                        ColliderCastResult2D resultItem = new ColliderCastResult2D
                        {
                            Hit = true,
                            Collider = top.collider
                        };
                        if (!collector.OnHit(resultItem))
                        {
                            return;
                        }
                    }
                    continue;
                }

                if (top.left >= 0)
                {
                    stack[stackCount++] = GetNode(top.left);
                }

                if (top.right >= 0)
                {
                    stack[stackCount++] = GetNode(top.right);
                }
            }
        }


        private void BuildBottomTop(ReadOnlySpan<ColliderRef2D> colliders)
        {
            _nodeSize = 0;

            if (colliders.Length == 0)
            {
                return;
            }

            if (colliders.Length == 1)
            {
                AddNode(CreateLeaf(colliders[0]));
                _root = _nodes[0];
                return;
            }

            StartJobBuildLeaf(colliders);
            BuildBranch();
        }

        private void BuildBranch()
        {
            int start = 0;
            int end = _nodeSize;

            Node* ptr = _nodes.UnsafePointer;

            while (start < end - 2)
            {
                int parentCount = (end - start + 1) / 2;
                for (int i = 0; i < parentCount; i++)
                {
                    int left = start + i * 2;
                    int right = start + i * 2 + 1;

                    if (right >= end)
                    {
                        ptr[end + i] = CreateParent(left);
                    }
                    else
                    {
                        ptr[end + i] = CreateParent(left, right);
                    }
                }

                start = end;
                end = start + parentCount;
                _nodeSize += parentCount;
            }

            if (end - start == 2)
            {
                _root = CreateParent(start, start + 1);
                AddNode(_root);
            }
        }

        private Node CreateParent(int singleChild)
        {
            return new Node
            {
                left = singleChild,
                right = -1,
                boundingBox = GetNode(singleChild).boundingBox
            };
        }

        private Node CreateParent(int left, int right)
        {
            return new Node
            {
                left = left,
                right = right,
                boundingBox = BoundingBox2D.Merge(GetNode(left).boundingBox, GetNode(right).boundingBox)
            };
        }

        private Node CreateLeaf(ColliderRef2D collider)
        {
            return new Node
            {
                left = -1,
                right = -1,
                collider = collider,
                boundingBox = collider.GetBoundingBox(),
            };
        }

        private void StartJobBuildLeaf(ReadOnlySpan<ColliderRef2D> colliders)
        {
            Node* ptr = _nodes.UnsafePointer;

            for (int i = 0; i < colliders.Length; i++)
            {
                ptr[i].left = -1;
                ptr[i].right = -1;
                ptr[i].collider = colliders[i];
                ptr[i].boundingBox = colliders[i].GetBoundingBox();
            }

            _nodeSize = colliders.Length;

            // new JobBuildLeaf
            // {
            //     nodeList = _nodes.Ptr,
            // }.Run(colliders.Length);
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

        private void AddNode(Node node)
        {
            if (_nodeSize >= _nodes.Length)
            {
                return;
            }
            _nodes.UnsafePointer[_nodeSize] = node;
            _nodeSize++;
        }


    }
}