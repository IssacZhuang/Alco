using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;

namespace Alco
{
    /// <summary>
    /// Flat 4-wide SIMD AABB list storing items in contiguous 80-byte <see cref="BvhAabb2D.Leaf"/> blocks
    /// without internal nodes or Morton sorting. Designed for tiny buckets or sparse chunks where linear
    /// SIMD scanning beats full tree construction in both build speed and memory footprint.
    /// </summary>
    public sealed unsafe class FlatSimdBucket2D : IDisposable
    {
        private const int Width = BvhAabb2D.Width;
        private static readonly BoundingBox2D EmptyBox = new(new Vector2(float.MaxValue), new Vector2(float.MinValue));

        private struct ListLeafCollector : IBvhAabb2DLeafVisitor
        {
            private readonly List<int> _results;

            public ListLeafCollector(List<int> results) => _results = results;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool OnLeaf(int payload)
            {
                _results.Add(payload);
                return true;
            }
        }

        private BvhAabb2D.Leaf* _blocks;
        private int _blockCapacity;
        private int _blockCount;
        private int _itemCount;
        private bool _isDisposed;

        /// <summary>Number of items stored in the bucket.</summary>
        public int Count => _itemCount;

        /// <summary>Number of 4-lane SIMD blocks currently populated.</summary>
        public int BlockCount => _blockCount;

        /// <summary>Exact native unmanaged bytes currently allocated for blocks.</summary>
        public long NativeMemoryBytes => (long)_blockCapacity * sizeof(BvhAabb2D.Leaf);

        /// <summary>
        /// Rebuilds the flat SIMD bucket over <paramref name="bounds"/> with sequential payloads <c>0..bounds.Length-1</c>.
        /// </summary>
        public void Build(ReadOnlySpan<BoundingBox2D> bounds)
        {
            BuildCore(bounds, ReadOnlySpan<int>.Empty, hasExplicitPayloads: false);
        }

        /// <summary>
        /// Rebuilds the flat SIMD bucket over <paramref name="bounds"/> and explicit integer <paramref name="payloads"/>.
        /// </summary>
        public void Build(ReadOnlySpan<BoundingBox2D> bounds, ReadOnlySpan<int> payloads)
        {
            if (payloads.Length < bounds.Length)
            {
                throw new ArgumentException("Payloads span length must be at least bounds span length.", nameof(payloads));
            }
            BuildCore(bounds, payloads, hasExplicitPayloads: true);
        }

        /// <summary>
        /// Frees unmanaged block storage and resets the bucket to empty.
        /// </summary>
        public void Clear()
        {
            if (_blocks != null)
            {
                MemoryUtility.Free(_blocks);
                _blocks = null;
                _blockCapacity = 0;
            }
            _blockCount = 0;
            _itemCount = 0;
        }

        private void BuildCore(ReadOnlySpan<BoundingBox2D> bounds, ReadOnlySpan<int> payloads, bool hasExplicitPayloads)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);

            int n = bounds.Length;
            if (n == 0)
            {
                Clear();
                return;
            }

            int blockCount = (n + (Width - 1)) / Width;
            if (_blockCapacity != blockCount)
            {
                if (_blocks != null)
                {
                    MemoryUtility.Free(_blocks);
                    _blocks = null;
                }
                _blockCapacity = blockCount;
                _blocks = MemoryUtility.Alloc<BvhAabb2D.Leaf>(blockCount);
            }

            for (int b = 0; b < blockCount; b++)
            {
                int i0 = b * Width;
                int i1 = i0 + 1;
                int i2 = i0 + 2;
                int i3 = i0 + 3;

                BoundingBox2D b0 = bounds[i0];
                BoundingBox2D b1 = i1 < n ? bounds[i1] : EmptyBox;
                BoundingBox2D b2 = i2 < n ? bounds[i2] : EmptyBox;
                BoundingBox2D b3 = i3 < n ? bounds[i3] : EmptyBox;

                int p0 = hasExplicitPayloads ? ValidatePayload(payloads[i0]) : i0;
                int p1 = i1 < n ? (hasExplicitPayloads ? ValidatePayload(payloads[i1]) : i1) : BvhAabb2D.EmptyLeafPayload;
                int p2 = i2 < n ? (hasExplicitPayloads ? ValidatePayload(payloads[i2]) : i2) : BvhAabb2D.EmptyLeafPayload;
                int p3 = i3 < n ? (hasExplicitPayloads ? ValidatePayload(payloads[i3]) : i3) : BvhAabb2D.EmptyLeafPayload;

                BvhAabb2D.Leaf* block = _blocks + b;
                block->LowerX = Vector128.Create(b0.Min.X, b1.Min.X, b2.Min.X, b3.Min.X);
                block->UpperX = Vector128.Create(b0.Max.X, b1.Max.X, b2.Max.X, b3.Max.X);
                block->LowerY = Vector128.Create(b0.Min.Y, b1.Min.Y, b2.Min.Y, b3.Min.Y);
                block->UpperY = Vector128.Create(b0.Max.Y, b1.Max.Y, b2.Max.Y, b3.Max.Y);
                block->Indices[0] = p0;
                block->Indices[1] = p1;
                block->Indices[2] = p2;
                block->Indices[3] = p3;
            }

            _blockCount = blockCount;
            _itemCount = n;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int ValidatePayload(int payload)
        {
            if (payload == BvhAabb2D.EmptyLeafPayload)
            {
                throw new ArgumentException("Payload cannot equal BvhAabb2D.EmptyLeafPayload.", nameof(payload));
            }
            return payload;
        }

        /// <summary>Finds all payloads whose AABB contains <paramref name="point"/>.</summary>
        public void QueryPoint(Vector2 point, List<int> results)
        {
            ListLeafCollector collector = new(results);
            VisitPoint(point, ref collector);
        }

        /// <summary>Visits all payloads whose AABB contains <paramref name="point"/> without heap allocation.</summary>
        public void VisitPoint<TVisitor>(Vector2 point, ref TVisitor visitor)
            where TVisitor : IBvhAabb2DLeafVisitor, allows ref struct
        {
            if (_blockCount == 0)
            {
                return;
            }

            Vector128<float> px = Vector128.Create(point.X);
            Vector128<float> py = Vector128.Create(point.Y);
            BvhAabb2D.Leaf* blocks = _blocks;

            for (int b = 0; b < _blockCount; b++)
            {
                BvhAabb2D.Leaf* block = blocks + b;
                Vector128<float> valid = Vector128.BitwiseAnd(
                    Vector128.GreaterThanOrEqual(block->UpperX, px),
                    Vector128.LessThanOrEqual(block->LowerX, px));
                valid = Vector128.BitwiseAnd(valid, Vector128.BitwiseAnd(
                    Vector128.GreaterThanOrEqual(block->UpperY, py),
                    Vector128.LessThanOrEqual(block->LowerY, py)));
                int mask = (int)valid.ExtractMostSignificantBits();
                for (int i = 0; i < Width; i++)
                {
                    if ((mask & (1 << i)) != 0)
                    {
                        int payload = block->Indices[i];
                        if (payload != BvhAabb2D.EmptyLeafPayload && !visitor.OnLeaf(payload))
                        {
                            return;
                        }
                    }
                }
            }
        }

        /// <summary>Finds all payloads whose AABB overlaps <paramref name="query"/>.</summary>
        public void OverlapAabb(in BoundingBox2D query, List<int> results)
        {
            ListLeafCollector collector = new(results);
            VisitOverlapAabb(query, ref collector);
        }

        /// <summary>Visits all payloads whose AABB overlaps <paramref name="query"/> without heap allocation.</summary>
        public void VisitOverlapAabb<TVisitor>(in BoundingBox2D query, ref TVisitor visitor)
            where TVisitor : IBvhAabb2DLeafVisitor, allows ref struct
        {
            if (_blockCount == 0)
            {
                return;
            }

            Vector128<float> qMinX = Vector128.Create(query.Min.X);
            Vector128<float> qMinY = Vector128.Create(query.Min.Y);
            Vector128<float> qMaxX = Vector128.Create(query.Max.X);
            Vector128<float> qMaxY = Vector128.Create(query.Max.Y);
            BvhAabb2D.Leaf* blocks = _blocks;

            for (int b = 0; b < _blockCount; b++)
            {
                BvhAabb2D.Leaf* block = blocks + b;
                Vector128<float> valid = Vector128.BitwiseAnd(
                    Vector128.GreaterThanOrEqual(block->UpperX, qMinX),
                    Vector128.LessThanOrEqual(block->LowerX, qMaxX));
                valid = Vector128.BitwiseAnd(valid, Vector128.BitwiseAnd(
                    Vector128.GreaterThanOrEqual(block->UpperY, qMinY),
                    Vector128.LessThanOrEqual(block->LowerY, qMaxY)));
                int mask = (int)valid.ExtractMostSignificantBits();
                for (int i = 0; i < Width; i++)
                {
                    if ((mask & (1 << i)) != 0)
                    {
                        int payload = block->Indices[i];
                        if (payload != BvhAabb2D.EmptyLeafPayload && !visitor.OnLeaf(payload))
                        {
                            return;
                        }
                    }
                }
            }
        }

        /// <summary>Finds the closest payload whose AABB is entered by the ray segment within <c>[0, 1]</c>.</summary>
        public bool RayCastClosest(Vector2 origin, Vector2 displacement, out int hitIndex, out float hitT) =>
            RayCastClosest(origin, displacement, 1f, out hitIndex, out hitT);

        /// <summary>Finds the closest payload whose AABB is entered by the ray segment within <c>[0, maxT]</c>.</summary>
        public bool RayCastClosest(Vector2 origin, Vector2 displacement, float maxT, out int hitIndex, out float hitT)
        {
            hitIndex = -1;
            hitT = 0f;
            if (_blockCount == 0 || maxT < 0f)
            {
                return false;
            }

            float invX = displacement.X != 0f ? 1f / displacement.X : float.MaxValue;
            float invY = displacement.Y != 0f ? 1f / displacement.Y : float.MaxValue;
            bool posX = invX >= 0f;
            bool posY = invY >= 0f;
            Vector128<float> orgX = Vector128.Create(origin.X);
            Vector128<float> orgY = Vector128.Create(origin.Y);
            Vector128<float> rdirX = Vector128.Create(invX);
            Vector128<float> rdirY = Vector128.Create(invY);
            Vector128<float> zero = Vector128<float>.Zero;
            Vector128<float> maxTVec = Vector128.Create(maxT);
            BvhAabb2D.Leaf* blocks = _blocks;

            bool hasHit = false;
            float bestT = maxT;
            int bestPayload = BvhAabb2D.EmptyLeafPayload;

            for (int b = 0; b < _blockCount; b++)
            {
                BvhAabb2D.Leaf* block = blocks + b;
                Vector128<float> t0 = Vector128.Multiply(Vector128.Subtract(block->LowerX, orgX), rdirX);
                Vector128<float> t1 = Vector128.Multiply(Vector128.Subtract(block->UpperX, orgX), rdirX);
                Vector128<float> tMin = posX ? t0 : t1;
                Vector128<float> tMax = posX ? t1 : t0;
                t0 = Vector128.Multiply(Vector128.Subtract(block->LowerY, orgY), rdirY);
                t1 = Vector128.Multiply(Vector128.Subtract(block->UpperY, orgY), rdirY);
                tMin = Vector128.Max(tMin, posY ? t0 : t1);
                tMax = Vector128.Min(tMax, posY ? t1 : t0);
                Vector128<float> seg = Vector128.LessThanOrEqual(Vector128.Max(tMin, zero), Vector128.Min(tMax, maxTVec));
                int mask = (int)Vector128.BitwiseAnd(seg, Vector128.LessThanOrEqual(tMin, Vector128.Create(bestT))).ExtractMostSignificantBits();
                if (mask != 0)
                {
                    for (int i = 0; i < Width; i++)
                    {
                        if ((mask & (1 << i)) != 0)
                        {
                            int payload = block->Indices[i];
                            if (payload != BvhAabb2D.EmptyLeafPayload)
                            {
                                float t = tMin.GetElement(i);
                                if (t < bestT || (!hasHit && t <= maxT))
                                {
                                    hasHit = true;
                                    bestT = t;
                                    bestPayload = payload;
                                }
                            }
                        }
                    }
                }
            }

            if (hasHit)
            {
                hitIndex = bestPayload;
                hitT = bestT;
                return true;
            }
            return false;
        }

        /// <summary>Traverses all intersecting payloads within <c>[0, maxT]</c>, letting <paramref name="visitor"/> tighten <c>bestT</c>.</summary>
        public void VisitRayClosest<TVisitor>(Vector2 origin, Vector2 displacement, float maxT, ref TVisitor visitor)
            where TVisitor : IBvhAabb2DRayClosestVisitor, allows ref struct =>
            VisitRayClosest(origin, displacement, maxT, maxT, ref visitor);

        /// <summary>
        /// Traverses all intersecting payloads within <c>[0, segmentMaxT]</c> with an initial pruning bound
        /// <paramref name="initialBestT"/> (which may be negative after an origin-inside hit).
        /// </summary>
        public void VisitRayClosest<TVisitor>(Vector2 origin, Vector2 displacement, float segmentMaxT, float initialBestT, ref TVisitor visitor)
            where TVisitor : IBvhAabb2DRayClosestVisitor, allows ref struct
        {
            if (_blockCount == 0 || segmentMaxT < 0f)
            {
                return;
            }

            float invX = displacement.X != 0f ? 1f / displacement.X : float.MaxValue;
            float invY = displacement.Y != 0f ? 1f / displacement.Y : float.MaxValue;
            bool posX = invX >= 0f;
            bool posY = invY >= 0f;
            Vector128<float> orgX = Vector128.Create(origin.X);
            Vector128<float> orgY = Vector128.Create(origin.Y);
            Vector128<float> rdirX = Vector128.Create(invX);
            Vector128<float> rdirY = Vector128.Create(invY);
            Vector128<float> zero = Vector128<float>.Zero;
            Vector128<float> maxTVec = Vector128.Create(segmentMaxT);
            BvhAabb2D.Leaf* blocks = _blocks;

            float bestT = initialBestT;
            for (int b = 0; b < _blockCount; b++)
            {
                BvhAabb2D.Leaf* block = blocks + b;
                Vector128<float> t0 = Vector128.Multiply(Vector128.Subtract(block->LowerX, orgX), rdirX);
                Vector128<float> t1 = Vector128.Multiply(Vector128.Subtract(block->UpperX, orgX), rdirX);
                Vector128<float> tMin = posX ? t0 : t1;
                Vector128<float> tMax = posX ? t1 : t0;
                t0 = Vector128.Multiply(Vector128.Subtract(block->LowerY, orgY), rdirY);
                t1 = Vector128.Multiply(Vector128.Subtract(block->UpperY, orgY), rdirY);
                tMin = Vector128.Max(tMin, posY ? t0 : t1);
                tMax = Vector128.Min(tMax, posY ? t1 : t0);
                Vector128<float> seg = Vector128.LessThanOrEqual(Vector128.Max(tMin, zero), Vector128.Min(tMax, maxTVec));
                int mask = (int)Vector128.BitwiseAnd(seg, Vector128.LessThanOrEqual(tMin, Vector128.Create(bestT))).ExtractMostSignificantBits();
                if (mask != 0)
                {
                    for (int i = 0; i < Width; i++)
                    {
                        if ((mask & (1 << i)) != 0)
                        {
                            int payload = block->Indices[i];
                            if (payload != BvhAabb2D.EmptyLeafPayload)
                            {
                                float entryT = tMin.GetElement(i);
                                if (entryT <= bestT)
                                {
                                    float nextBest = visitor.OnLeaf(payload, entryT, bestT);
                                    if (nextBest < bestT)
                                    {
                                        bestT = nextBest;
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        /// <summary>Returns true if any leaf AABB intersects the ray segment within <c>[0, maxT]</c>.</summary>
        public bool RayCastAny(Vector2 origin, Vector2 displacement, float maxT = 1f)
        {
            if (_blockCount == 0 || maxT < 0f)
            {
                return false;
            }

            float invX = displacement.X != 0f ? 1f / displacement.X : float.MaxValue;
            float invY = displacement.Y != 0f ? 1f / displacement.Y : float.MaxValue;
            bool posX = invX >= 0f;
            bool posY = invY >= 0f;
            Vector128<float> orgX = Vector128.Create(origin.X);
            Vector128<float> orgY = Vector128.Create(origin.Y);
            Vector128<float> rdirX = Vector128.Create(invX);
            Vector128<float> rdirY = Vector128.Create(invY);
            Vector128<float> zero = Vector128<float>.Zero;
            Vector128<float> maxTVec = Vector128.Create(maxT);
            BvhAabb2D.Leaf* blocks = _blocks;

            for (int b = 0; b < _blockCount; b++)
            {
                BvhAabb2D.Leaf* block = blocks + b;
                Vector128<float> t0 = Vector128.Multiply(Vector128.Subtract(block->LowerX, orgX), rdirX);
                Vector128<float> t1 = Vector128.Multiply(Vector128.Subtract(block->UpperX, orgX), rdirX);
                Vector128<float> tMin = posX ? t0 : t1;
                Vector128<float> tMax = posX ? t1 : t0;
                t0 = Vector128.Multiply(Vector128.Subtract(block->LowerY, orgY), rdirY);
                t1 = Vector128.Multiply(Vector128.Subtract(block->UpperY, orgY), rdirY);
                tMin = Vector128.Max(tMin, posY ? t0 : t1);
                tMax = Vector128.Min(tMax, posY ? t1 : t0);
                int mask = (int)Vector128.LessThanOrEqual(Vector128.Max(tMin, zero), Vector128.Min(tMax, maxTVec)).ExtractMostSignificantBits();
                for (int i = 0; i < Width; i++)
                {
                    if ((mask & (1 << i)) != 0 && block->Indices[i] != BvhAabb2D.EmptyLeafPayload)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>Visits all payloads whose AABB intersects the ray segment within <c>[0, maxT]</c>.</summary>
        public void VisitRayAll<TVisitor>(Vector2 origin, Vector2 displacement, float maxT, ref TVisitor visitor)
            where TVisitor : IBvhAabb2DLeafVisitor, allows ref struct
        {
            if (_blockCount == 0 || maxT < 0f)
            {
                return;
            }

            float invX = displacement.X != 0f ? 1f / displacement.X : float.MaxValue;
            float invY = displacement.Y != 0f ? 1f / displacement.Y : float.MaxValue;
            bool posX = invX >= 0f;
            bool posY = invY >= 0f;
            Vector128<float> orgX = Vector128.Create(origin.X);
            Vector128<float> orgY = Vector128.Create(origin.Y);
            Vector128<float> rdirX = Vector128.Create(invX);
            Vector128<float> rdirY = Vector128.Create(invY);
            Vector128<float> zero = Vector128<float>.Zero;
            Vector128<float> maxTVec = Vector128.Create(maxT);
            BvhAabb2D.Leaf* blocks = _blocks;

            for (int b = 0; b < _blockCount; b++)
            {
                BvhAabb2D.Leaf* block = blocks + b;
                Vector128<float> t0 = Vector128.Multiply(Vector128.Subtract(block->LowerX, orgX), rdirX);
                Vector128<float> t1 = Vector128.Multiply(Vector128.Subtract(block->UpperX, orgX), rdirX);
                Vector128<float> tMin = posX ? t0 : t1;
                Vector128<float> tMax = posX ? t1 : t0;
                t0 = Vector128.Multiply(Vector128.Subtract(block->LowerY, orgY), rdirY);
                t1 = Vector128.Multiply(Vector128.Subtract(block->UpperY, orgY), rdirY);
                tMin = Vector128.Max(tMin, posY ? t0 : t1);
                tMax = Vector128.Min(tMax, posY ? t1 : t0);
                int mask = (int)Vector128.LessThanOrEqual(Vector128.Max(tMin, zero), Vector128.Min(tMax, maxTVec)).ExtractMostSignificantBits();
                for (int i = 0; i < Width; i++)
                {
                    if ((mask & (1 << i)) != 0)
                    {
                        int payload = block->Indices[i];
                        if (payload != BvhAabb2D.EmptyLeafPayload && !visitor.OnLeaf(payload))
                        {
                            return;
                        }
                    }
                }
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            Clear();
            _isDisposed = true;
        }
    }
}
