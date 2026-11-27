using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Alco
{
    /// <summary>
    /// Unbounded two-level static/semi-static 2D collision broadphase built on <see cref="ChunkedBvhAabb2D"/>
    /// with single-owner signed <c>int2</c> chunks, immediate pre-commit query visibility,
    /// generation-guarded handle disposal, and <see cref="UnloadChunk"/> streaming eviction.
    /// </summary>
    /// <remarks>
    /// Mutation methods (<see cref="Add(object, in ShapeBox2D)"/>, <see cref="Add(object, in ShapeSphere2D)"/>,
    /// <see cref="TargetRegistration.Dispose"/>, <see cref="UnloadChunk"/>, <see cref="Clear"/>,
    /// <see cref="RebuildDirtyTrees"/>) must run exclusively from query methods.
    /// Once <see cref="RebuildDirtyTrees"/> has committed pending edits, read queries are thread-safe.
    /// </remarks>
    public sealed unsafe class ChunkedCollisionWorld2D : AutoDisposable
    {
        private struct RegistrationData
        {
            public ColliderType2D Type;
            public bool Allocated;
            public int Generation;
            public ChunkedEntryHandle2D BvhHandle;
            public object? Target;
            public BoundingBox2D Bounds;
            public ShapeBox2D Box;
            public ShapeSphere2D Sphere;
            public int FreeNext;
        }

        /// <summary>
        /// Generation-guarded registration handle for a collider registered in <see cref="ChunkedCollisionWorld2D"/>.
        /// Disposing after <see cref="UnloadChunk"/> or <see cref="Clear"/> is safely a no-op.
        /// </summary>
        public readonly struct TargetRegistration : IDisposable
        {
            private readonly ChunkedCollisionWorld2D? _world;
            private readonly int _regIndex;
            private readonly int _generation;

            /// <summary>Gets whether this handle was constructed with a valid registration slot.</summary>
            public bool IsValid => _world != null && _generation > 0;

            /// <summary>
            /// Initializes a registration handle bound to a world slot and generation.
            /// </summary>
            public TargetRegistration(ChunkedCollisionWorld2D world, int regIndex, int generation)
            {
                _world = world;
                _regIndex = regIndex;
                _generation = generation;
            }

            /// <summary>
            /// Removes the collider from the world if this handle is still active.
            /// </summary>
            public void Dispose()
            {
                _world?.RemoveRegistration(_regIndex, _generation);
            }
        }

        private struct PointNarrowphaseVisitor<TCollector> : IBvhAabb2DLeafVisitor<TCollector>
            where TCollector : ICollisionCastCollector
        {
            private readonly ChunkedCollisionWorld2D _world;
            private readonly Vector2 _point;

            public PointNarrowphaseVisitor(ChunkedCollisionWorld2D world, Vector2 point)
            {
                _world = world;
                _point = point;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool OnLeaf(int regIndex, ref TCollector collector)
            {
                if (!_world.LeafContainsPoint(regIndex, _point))
                {
                    return true;
                }

                object? target = _world._registrations[regIndex].Target;
                return target == null || collector.OnHit(target);
            }
        }

        private struct ShapeNarrowphaseVisitor<TCollector> : IBvhAabb2DLeafVisitor<(ChunkedCollisionWorld2D World, ColliderRef2D Caster, TCollector Collector)>
            where TCollector : ICollisionCastCollector
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool OnLeaf(int regIndex, ref (ChunkedCollisionWorld2D World, ColliderRef2D Caster, TCollector Collector) state)
            {
                if (!state.World.LeafCollidesWith(regIndex, state.Caster))
                {
                    return true;
                }

                object? target = state.World._registrations[regIndex].Target;
                return target == null || state.Collector.OnHit(target);
            }
        }

        private struct RayAllNarrowphaseVisitor<TCollector> : IBvhAabb2DLeafVisitor<(ChunkedCollisionWorld2D World, Ray2D Ray, float MaxT, TCollector Collector)>
            where TCollector : IRayCastCollector2D
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool OnLeaf(int regIndex, ref (ChunkedCollisionWorld2D World, Ray2D Ray, float MaxT, TCollector Collector) state)
            {
                if (!state.World.LeafIntersectRay(regIndex, state.Ray, state.MaxT, out RaycastHit2D hit))
                {
                    return true;
                }

                object? target = state.World._registrations[regIndex].Target;
                return target == null || state.Collector.OnHit(target, hit);
            }
        }

        private struct RayClosestState
        {
            public readonly ChunkedCollisionWorld2D World;
            public readonly Ray2D Ray;
            public bool Hit;
            public RaycastHit2D BestHit;
            public int BestRegIndex;

            public RayClosestState(ChunkedCollisionWorld2D world, in Ray2D ray)
            {
                World = world;
                Ray = ray;
                Hit = false;
                BestHit = default;
                BestRegIndex = -1;
            }
        }

        private struct RayClosestNarrowphaseVisitor : IBvhAabb2DRayClosestVisitor<RayClosestState>
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public float OnLeaf(int regIndex, float entryT, float currentBestT, ref RayClosestState state)
            {
                if (!state.World.LeafIntersectRay(regIndex, state.Ray, currentBestT, out RaycastHit2D hit))
                {
                    return currentBestT;
                }

                if (state.World._registrations[regIndex].Target == null)
                {
                    return currentBestT;
                }

                if (!state.Hit || hit.Fraction < state.BestHit.Fraction)
                {
                    state.Hit = true;
                    state.BestHit = hit;
                    state.BestRegIndex = regIndex;
                    return hit.Fraction;
                }

                return currentBestT;
            }
        }

        private struct BoundsCollectorVisitor<TCollector> : IBvhAabb2DLeafVisitor<TCollector>
            where TCollector : ICollisionCastCollector
        {
            private readonly ChunkedCollisionWorld2D _world;

            public BoundsCollectorVisitor(ChunkedCollisionWorld2D world) => _world = world;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool OnLeaf(int regIndex, ref TCollector collector)
            {
                object? target = _world._registrations[regIndex].Target;
                return target == null || collector.OnHit(target);
            }
        }

        private readonly struct ICollectionCastAdapter(ICollection<object> collector) : ICollisionCastCollector
        {
            private readonly ICollection<object> _collector = collector;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool OnHit(object target)
            {
                _collector.Add(target);
                return true;
            }
        }

        private readonly ChunkedBvhAabb2D _bvh;
        private readonly Action<int> _onBvhEntryRemoved;
        private RegistrationData[] _registrations = new RegistrationData[128];
        private int _highWaterMark;
        private int _freeHead = -1;

        /// <summary>Gets the world-space edge length of each square owner chunk.</summary>
        public int ChunkSize => _bvh.ChunkSize;

        /// <summary>Gets the entry count threshold above which chunks build a 20-bit BVH instead of a flat SIMD bucket.</summary>
        public int TreeBuildThreshold => _bvh.TreeBuildThreshold;

        /// <summary>Gets the top-level TLAS strategy used across chunks.</summary>
        public ChunkedTlasMode2D TlasMode => _bvh.TlasMode;

        /// <summary>Gets the number of active collider registrations currently in the world.</summary>
        public int Count => _bvh.EntryCount;

        /// <summary>Gets the number of non-empty spatial chunks currently loaded.</summary>
        public int ChunkCount => _bvh.ChunkCount;

        /// <summary>Gets the number of chunks with pending un-rebuilt edits.</summary>
        public int DirtyChunkCount => _bvh.DirtyChunkCount;

        /// <summary>Gets total unmanaged memory bytes used by backing BVH structures.</summary>
        public long NativeMemoryBytes => _bvh.NativeMemoryBytes;

        /// <summary>
        /// Initializes an unbounded <see cref="ChunkedCollisionWorld2D"/> backed by <see cref="ChunkedBvhAabb2D"/>.
        /// </summary>
        public ChunkedCollisionWorld2D(
            int chunkSize = ChunkUtility.DefaultChunkSize,
            int treeBuildThreshold = 16,
            ChunkedTlasMode2D tlasMode = ChunkedTlasMode2D.MortonSimd)
        {
            _bvh = new ChunkedBvhAabb2D(chunkSize, treeBuildThreshold, tlasMode);
            _onBvhEntryRemoved = ReleaseSlotOnUnload;
        }

        /// <summary>
        /// Registers a static oriented box collider in the world and returns its disposal handle.
        /// </summary>
        public TargetRegistration Add(object target, in ShapeBox2D shape)
        {
            BoundingBox2D bounds = shape.GetBoundingBox();
            int regIndex = AllocateSlot(out int generation);
            ChunkedEntryHandle2D handle = _bvh.Add(bounds, regIndex);

            _registrations[regIndex] = new RegistrationData
            {
                Type = ColliderType2D.Box,
                Allocated = true,
                Generation = generation,
                BvhHandle = handle,
                Target = target,
                Bounds = bounds,
                Box = shape,
                Sphere = default,
                FreeNext = -1,
            };

            return new TargetRegistration(this, regIndex, generation);
        }

        /// <summary>
        /// Registers a static circle collider in the world and returns its disposal handle.
        /// </summary>
        public TargetRegistration Add(object target, in ShapeSphere2D shape)
        {
            BoundingBox2D bounds = shape.GetBoundingBox();
            int regIndex = AllocateSlot(out int generation);
            ChunkedEntryHandle2D handle = _bvh.Add(bounds, regIndex);

            _registrations[regIndex] = new RegistrationData
            {
                Type = ColliderType2D.Sphere,
                Allocated = true,
                Generation = generation,
                BvhHandle = handle,
                Target = target,
                Bounds = bounds,
                Box = default,
                Sphere = shape,
                FreeNext = -1,
            };

            return new TargetRegistration(this, regIndex, generation);
        }

        /// <summary>
        /// Rebuilds only dirty chunk acceleration structures and the SIMD TLAS if needed.
        /// Queries remain accurate even before calling this method.
        /// </summary>
        public void RebuildDirtyTrees()
        {
            _bvh.CommitDirty();
        }

        /// <summary>
        /// Unloads an entire owner chunk at <paramref name="chunkCoord"/>, removing all colliders owned by that chunk
        /// and safely invalidating their outstanding <see cref="TargetRegistration"/> handles.
        /// Returns the number of colliders removed.
        /// </summary>
        public int UnloadChunk(int2 chunkCoord)
        {
            return _bvh.UnloadChunk(chunkCoord, _onBvhEntryRemoved);
        }

        /// <summary>
        /// Removes all registrations and resets chunk storage while invalidating any active handles.
        /// </summary>
        public void Clear()
        {
            _bvh.Clear();
            for (int i = 0; i < _highWaterMark; i++)
            {
                if (_registrations[i].Allocated)
                {
                    int nextGen = _registrations[i].Generation + 1;
                    _registrations[i].Generation = nextGen > 0 ? nextGen : 1;
                    _registrations[i].Allocated = false;
                    _registrations[i].Target = null;
                }
            }
            _highWaterMark = 0;
            _freeHead = -1;
        }

        /// <summary>Tests a world-space point against all static colliders and delivers exact hits to <paramref name="collector"/>.</summary>
        public void CastPoint<TCollector>(ref TCollector collector, Vector2 point) where TCollector : ICollisionCastCollector
        {
            if (_bvh.EntryCount == 0)
            {
                return;
            }

            PointNarrowphaseVisitor<TCollector> visitor = new(this, point);
            _bvh.VisitPoint(point, ref visitor, ref collector);
        }

        /// <summary>Tests an oriented box shape against all static colliders and delivers exact hits to <paramref name="collector"/>.</summary>
        public void CastBox<TCollector>(ref TCollector collector, in ShapeBox2D shape) where TCollector : ICollisionCastCollector
        {
            if (_bvh.EntryCount == 0)
            {
                return;
            }

            BoundingBox2D bounds = shape.GetBoundingBox();
            ColliderBox2D queryBox = new() { Shape = shape };
            ColliderRef2D caster = ColliderRef2D.Create(&queryBox);
            ShapeNarrowphaseVisitor<TCollector> visitor = default;
            var state = (World: this, Caster: caster, Collector: collector);
            _bvh.VisitOverlapAabb(bounds, ref visitor, ref state);
            collector = state.Collector;
        }

        /// <summary>Tests a circle shape against all static colliders and delivers exact hits to <paramref name="collector"/>.</summary>
        public void CastSphere<TCollector>(ref TCollector collector, in ShapeSphere2D shape) where TCollector : ICollisionCastCollector
        {
            if (_bvh.EntryCount == 0)
            {
                return;
            }

            BoundingBox2D bounds = shape.GetBoundingBox();
            ColliderSphere2D querySphere = new() { Shape = shape };
            ColliderRef2D caster = ColliderRef2D.Create(&querySphere);
            ShapeNarrowphaseVisitor<TCollector> visitor = default;
            var state = (World: this, Caster: caster, Collector: collector);
            _bvh.VisitOverlapAabb(bounds, ref visitor, ref state);
            collector = state.Collector;
        }

        /// <summary>Casts a ray segment up to <paramref name="maxT"/> and delivers all exact hits to <paramref name="collector"/>.</summary>
        public void CastRay<TCollector>(ref TCollector collector, in Ray2D ray, float maxT = 1f) where TCollector : IRayCastCollector2D
        {
            if (_bvh.EntryCount == 0 || maxT < 0f)
            {
                return;
            }

            RayAllNarrowphaseVisitor<TCollector> visitor = default;
            var state = (World: this, Ray: ray, MaxT: maxT, Collector: collector);
            _bvh.VisitRayAll(ray.Origin, ray.Displacement, maxT, ref visitor, ref state);
            collector = state.Collector;
        }

        /// <summary>Casts a ray segment up to <paramref name="maxT"/> and returns the closest hit target of type <typeparamref name="TTarget"/>.</summary>
        public bool TryCastRayClosestHit<TTarget>(in Ray2D ray, out TTarget? hitTarget, out RaycastHit2D hit, float maxT = 1f) where TTarget : class
        {
            hitTarget = null;
            hit = default;
            if (_bvh.EntryCount == 0 || maxT < 0f)
            {
                return false;
            }

            RayClosestState state = new(this, ray);
            RayClosestNarrowphaseVisitor visitor = default;
            _bvh.VisitRayClosest(ray.Origin, ray.Displacement, maxT, ref visitor, ref state);

            if (!state.Hit)
            {
                return false;
            }

            if (_registrations[state.BestRegIndex].Target is not TTarget typed)
            {
                return false;
            }

            hitTarget = typed;
            hit = state.BestHit;
            return true;
        }

        /// <summary>
        /// Collects all targets whose AABB overlaps <paramref name="bounds"/> without performing exact narrowphase shape tests.
        /// </summary>
        public void CollectTargets(BoundingBox2D bounds, ICollection<object> collector)
        {
            if (_bvh.EntryCount == 0)
            {
                return;
            }

            ICollectionCastAdapter adapter = new(collector);
            CollectTargets(bounds, ref adapter);
        }

        /// <summary>
        /// Zero-allocation generic overload collecting all targets whose AABB overlaps <paramref name="bounds"/>.
        /// </summary>
        public void CollectTargets<TCollector>(in BoundingBox2D bounds, ref TCollector collector) where TCollector : struct, ICollisionCastCollector
        {
            if (_bvh.EntryCount == 0)
            {
                return;
            }

            BoundsCollectorVisitor<TCollector> visitor = new(this);
            _bvh.VisitOverlapAabb(bounds, ref visitor, ref collector);
        }

        private void RemoveRegistration(int regIndex, int generation)
        {
            if ((uint)regIndex >= (uint)_highWaterMark)
            {
                return;
            }

            ref RegistrationData reg = ref _registrations[regIndex];
            if (!reg.Allocated || reg.Generation != generation)
            {
                return;
            }

            _bvh.Remove(reg.BvhHandle);
            ReleaseSlot(regIndex);
        }

        private void ReleaseSlotOnUnload(int regIndex)
        {
            if ((uint)regIndex < (uint)_highWaterMark && _registrations[regIndex].Allocated)
            {
                ReleaseSlot(regIndex);
            }
        }

        private void ReleaseSlot(int regIndex)
        {
            int nextGen = _registrations[regIndex].Generation + 1;
            _registrations[regIndex].Generation = nextGen > 0 ? nextGen : 1;
            _registrations[regIndex].Allocated = false;
            _registrations[regIndex].Target = null;
            _registrations[regIndex].BvhHandle = default;
            _registrations[regIndex].FreeNext = _freeHead;
            _freeHead = regIndex;
        }

        private int AllocateSlot(out int generation)
        {
            if (_freeHead >= 0)
            {
                int idx = _freeHead;
                _freeHead = _registrations[idx].FreeNext;
                generation = _registrations[idx].Generation > 0 ? _registrations[idx].Generation : 1;
                return idx;
            }

            if (_highWaterMark == _registrations.Length)
            {
                Array.Resize(ref _registrations, _registrations.Length * 2);
            }

            int slot = _highWaterMark++;
            generation = _registrations[slot].Generation > 0 ? _registrations[slot].Generation : 1;
            return slot;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool LeafContainsPoint(int regIndex, Vector2 point)
        {
            ref readonly RegistrationData reg = ref _registrations[regIndex];
            if (reg.Type == ColliderType2D.Box)
            {
                ColliderBox2D box = new() { Shape = reg.Box };
                return box.IntersectPoint(point);
            }

            ColliderSphere2D sphere = new() { Shape = reg.Sphere };
            return sphere.IntersectPoint(point);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool LeafCollidesWith(int regIndex, ColliderRef2D caster)
        {
            ref readonly RegistrationData reg = ref _registrations[regIndex];
            if (reg.Type == ColliderType2D.Box)
            {
                ColliderBox2D box = new() { Shape = reg.Box };
                ColliderRef2D leafRef = ColliderRef2D.Create(&box);
                return leafRef.CollidesWith(caster);
            }

            ColliderSphere2D sphere = new() { Shape = reg.Sphere };
            ColliderRef2D sphereRef = ColliderRef2D.Create(&sphere);
            return sphereRef.CollidesWith(caster);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool LeafIntersectRay(int regIndex, in Ray2D ray, float maxT, out RaycastHit2D hit)
        {
            ref readonly RegistrationData reg = ref _registrations[regIndex];
            bool hitResult;
            if (reg.Type == ColliderType2D.Box)
            {
                ColliderBox2D box = new() { Shape = reg.Box };
                hitResult = box.IntersectRay(ray, out hit);
            }
            else
            {
                ColliderSphere2D sphere = new() { Shape = reg.Sphere };
                hitResult = sphere.IntersectRay(ray, out hit);
            }

            return hitResult && hit.Fraction <= maxT;
        }

        /// <summary>
        /// Disposes managed and unmanaged resources held by the backing spatial index.
        /// </summary>
        /// <param name="disposing">True when called from <see cref="IDisposable.Dispose"/>.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Clear();
                _bvh.Dispose();
            }
        }
    }
}
