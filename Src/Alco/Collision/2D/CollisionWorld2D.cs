using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using Alco;

namespace Alco;

/// <summary>
/// 2D collision world supporting both collider casting and ray casting against a BVH of targets.
/// </summary>
public unsafe class CollisionWorld2D : AutoDisposable
{

    private readonly NativeBvh2D _bvh;

    public NativeBvh2D Bvh => _bvh;

    //the index of the target in the list is the index of the collider in the list
    private readonly List<object> _targets = new List<object>();


    private MiniHeap<ColliderBox2D> _targetBoxes;
    private MiniHeap<ColliderSphere2D> _targetSpheres;
    private NativeArrayList<ColliderRef2D> _targetColliders;


    public CollisionWorld2D()
    {
        _bvh = new NativeBvh2D();
    }

    /// <summary>
    /// Add object that waits for being hit. This object can only be hit but cannot hit other objects.
    /// </summary>
    /// /// <param name="target"> Object that waits for being hit. </param>
    /// <param name="shape"> Shape of the object. </param>
    public void PushCollisionTarget(object target, in ShapeBox2D shape)
    {
        ColliderBox2D* collider = _targetBoxes.Alloc(new ColliderBox2D
        {
            Shape = shape
        });

        PushTargetCore(target, collider);
    }

    /// <summary>
    /// Add object that waits for being hit. This object can only be hit but cannot hit other objects.
    /// </summary>
    /// <param name="target"> Object that waits for being hit. </param>
    /// <param name="shape"> Shape of the object. </param>    
    public void PushCollisionTarget(object target, in ShapeSphere2D shape)
    {
        ColliderSphere2D* collider = _targetSpheres.Alloc(new ColliderSphere2D
        {
            Shape = shape
        });

        PushTargetCore(target, collider);
    }

    /// <summary>
    /// Build the BVH tree for the targets.
    /// </summary>
    public void BuildTree()
    {
        _bvh.BuildTree(_targetColliders.AsSpan());
    }

    /// <summary>
    /// Casts a collider against the world and collects hits using the provided collector.
    /// </summary>
    /// <typeparam name="TCollector">The type of the collector.</typeparam>
    /// <param name="collider">The collider to cast.</param>
    /// <param name="collector">The collector to gather hit results.</param>
    public void CastCollider<TCollector>(ColliderRef2D collider, ref TCollector collector) where TCollector : struct, ICollisionCollector
    {
        ObjectCollectorAdapter<TCollector> adapter = new ObjectCollectorAdapter<TCollector>(_targets, collector);
        _bvh.CastCollider(collider, ref adapter);
        collector = adapter.UserCollector;
    }

    /// <summary>
    /// Casts a sphere collider against the world and collects hits using the provided collector.
    /// </summary>
    /// <typeparam name="TCollector">The type of the collector.</typeparam>
    /// <param name="collector">The collector to gather hit results.</param>
    /// <param name="shape">The sphere shape to cast.</param>
    public void CastSphere<TCollector>(ref TCollector collector, in ShapeSphere2D shape) where TCollector : struct, ICollisionCollector
    {
        ColliderSphere2D collider = new ColliderSphere2D { Shape = shape };
        ColliderRef2D colliderRef = ColliderRef2D.Create(&collider);
        CastCollider(colliderRef, ref collector);
    }

    /// <summary>
    /// Casts a box collider against the world and collects hits using the provided collector.
    /// </summary>
    /// <typeparam name="TCollector">The type of the collector.</typeparam>
    /// <param name="collector">The collector to gather hit results.</param>
    /// <param name="shape">The box shape to cast.</param>
    public void CastBox<TCollector>(ref TCollector collector, in ShapeBox2D shape) where TCollector : struct, ICollisionCollector
    {
        ColliderBox2D collider = new ColliderBox2D { Shape = shape };
        ColliderRef2D colliderRef = ColliderRef2D.Create(&collider);
        CastCollider(colliderRef, ref collector);
    }

    /// <summary>
    /// Casts a point against the world and collects hits using the provided collector.
    /// </summary>
    /// <typeparam name="TCollector">The type of the collector.</typeparam>
    /// <param name="point">The point to cast.</param>
    /// <param name="collector">The collector to gather hit results.</param>
    public void CastPoint<TCollector>(Vector2 point, ref TCollector collector) where TCollector : struct, ICollisionCollector
    {
        ObjectCollectorAdapter<TCollector> adapter = new ObjectCollectorAdapter<TCollector>(_targets, collector);
        _bvh.CastPoint(point, ref adapter);
        collector = adapter.UserCollector;
    }


    /// <summary>
    /// Casts a 2D oriented box shape and collects hit targets directly into a provided set.
    /// </summary>
    public void CastBox<TTarget>(ISet<TTarget> collector, in ShapeBox2D shape) where TTarget : class
    {
        ColliderBox2D collider = new ColliderBox2D { Shape = shape };
        ColliderRef2D colliderRef = ColliderRef2D.Create(&collider);

        var adapter = new SetCollector<TTarget>(collector);
        CastCollider(colliderRef, ref adapter);
    }

    /// <summary>
    /// Casts a 2D oriented box shape and collects hit targets into a provided collection.
    /// </summary>
    public void CastBox<TTarget>(ICollection<TTarget> collector, in ShapeBox2D shape) where TTarget : class
    {
        ColliderBox2D collider = new ColliderBox2D { Shape = shape };
        ColliderRef2D colliderRef = ColliderRef2D.Create(&collider);

        var adapter = new CollectionCollector<TTarget>(collector);
        CastCollider(colliderRef, ref adapter);
    }

    /// <summary>
    /// Casts a 2D sphere shape and collects hit targets directly into a provided set.
    /// </summary>
    public void CastSphere<TTarget>(ISet<TTarget> collector, in ShapeSphere2D shape) where TTarget : class
    {
        ColliderSphere2D collider = new ColliderSphere2D { Shape = shape };
        ColliderRef2D colliderRef = ColliderRef2D.Create(&collider);

        var adapter = new SetCollector<TTarget>(collector);
        CastCollider(colliderRef, ref adapter);
    }

    /// <summary>
    /// Casts a 2D sphere shape and collects hit targets into a provided collection.
    /// </summary>
    public void CastSphere<TTarget>(ICollection<TTarget> collector, in ShapeSphere2D shape) where TTarget : class
    {
        ColliderSphere2D collider = new ColliderSphere2D { Shape = shape };
        ColliderRef2D colliderRef = ColliderRef2D.Create(&collider);

        var adapter = new CollectionCollector<TTarget>(collector);
        CastCollider(colliderRef, ref adapter);
    }

    /// <summary>
    /// Casts a point and collects hit targets directly into a provided set.
    /// </summary>
    public void CastPoint<TTarget>(ISet<TTarget> collector, in Vector2 point) where TTarget : class
    {
        var adapter = new SetCollector<TTarget>(collector);
        CastPoint(point, ref adapter);
    }

    /// <summary>
    /// Casts a point and collects hit targets into a provided collection.
    /// </summary>
    public void CastPoint<TTarget>(ICollection<TTarget> collector, in Vector2 point) where TTarget : class
    {
        var adapter = new CollectionCollector<TTarget>(collector);
        CastPoint(point, ref adapter);
    }

    /// <summary>
    /// Casts a ray against targets and returns the first hit.
    /// </summary>
    public bool TryCastRayFirstHit<TTarget>(in Ray2D ray, out TTarget? hitTarget, out RaycastHit2D hit) where TTarget : class
    {
        hitTarget = null;
        hit = default;

        RayCastResult2D result = _bvh.CastRayFirstHit(ray);
        if (!result.Hit)
        {
            return false;
        }

        object obj = _targets[result.Collider.UserData];
        if (obj is TTarget t)
        {
            hitTarget = t;
            hit = result.HitInfo;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Casts a ray against targets and returns the closest hit.
    /// </summary>
    public bool TryCastRay<TTarget>(in Ray2D ray, out TTarget? hitTarget, out RaycastHit2D hit) where TTarget : class
    {
        hitTarget = null;
        hit = default;

        RayCastResult2D result = _bvh.CastRay(ray);
        if (!result.Hit)
        {
            return false;
        }

        object obj = _targets[result.Collider.UserData];
        if (obj is TTarget t)
        {
            hitTarget = t;
            hit = result.HitInfo;
            return true;
        }

        return false;
    }

    public void ClearTargets()
    {
        _targets.Clear();
        _targetBoxes.Reset();
        _targetSpheres.Reset();
        _targetColliders.Clear();
    }

    public void ClearAll()
    {
        ClearTargets();
    }

    private void PushTargetCore<T>(object target, T* collider) where T : unmanaged, ICollider2D
    {
        _targets.Add(target);
        int targetIndex = _targets.Count - 1;
        ColliderRef2D colliderRef = ColliderRef2D.Create(collider);
        colliderRef.UserData = targetIndex;
        _targetColliders.Add(colliderRef);
    }

    protected override void Dispose(bool disposing)
    {
        _bvh.Dispose();

        _targetBoxes.Dispose();
        _targetSpheres.Dispose();
        _targetColliders.Dispose();

        _targets.Clear();
    }

    // --- Internal Adapters ---

    private struct ObjectCollectorAdapter<TUserCollector> : IBvhCollisionCollector
        where TUserCollector : ICollisionCollector
    {
        private readonly List<object> _targets;
        public TUserCollector UserCollector;

        public ObjectCollectorAdapter(List<object> targets, TUserCollector userCollector)
        {
            _targets = targets;
            UserCollector = userCollector;
        }

        public bool OnHit(ColliderCastResult2D result)
        {
            int id = result.Collider.UserData;
            object target = _targets[id];
            return UserCollector.OnHit(target);
        }
    }

    private struct SetCollector<T> : ICollisionCollector where T : class
    {
        private readonly ISet<T> _set;

        public SetCollector(ISet<T> set)
        {
            _set = set;
        }

        public bool OnHit(object target)
        {
            if (target is T t)
            {
                _set.Add(t);
            }
            return true;
        }
    }

    private struct CollectionCollector<T> : ICollisionCollector where T : class
    {
        private readonly ICollection<T> _collection;

        public CollectionCollector(ICollection<T> collection)
        {
            _collection = collection;
        }

        public bool OnHit(object target)
        {
            if (target is T t)
            {
                _collection.Add(t);
            }
            return true;
        }
    }
}
