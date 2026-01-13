using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.InteropServices;
using Alco;

namespace Alco;

/// <summary>
/// 3D collision world supporting both collider casting and ray casting against a BVH of targets.
/// </summary>
public unsafe class CollisionWorld3D : AutoDisposable
{

    private readonly NativeBvh3D _bvh;

    public NativeBvh3D Bvh => _bvh;

    //the index of the target in the list is the index of the collider in the list
    private readonly List<object> _targets = new List<object>();


    private MiniHeap<ColliderBox3D> _targetBoxes;
    private MiniHeap<ColliderSphere3D> _targetSpheres;
    private NativeArrayList<ColliderRef3D> _targetColliders;


    public CollisionWorld3D()
    {
        _bvh = new NativeBvh3D();
    }

    /// <summary>
    /// Add object that waits for being hit. This object can only be hit but cannot hit other objects.
    /// </summary>
    /// /// <param name="target"> Object that waits for being hit. </param>
    /// <param name="shape"> Shape of the object. </param>
    public void PushCollisionTarget(object target, in ShapeBox3D shape)
    {
        ColliderBox3D* collider = _targetBoxes.Alloc(new ColliderBox3D
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
    public void PushCollisionTarget(object target, in ShapeSphere3D shape)
    {
        ColliderSphere3D* collider = _targetSpheres.Alloc(new ColliderSphere3D
        {
            shape = shape
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
    /// Casts a sphere collider against the world and collects hits using the provided collector.
    /// </summary>
    /// <typeparam name="TCollector">The type of the collector.</typeparam>
    /// <param name="collector">The collector to gather hit results.</param>
    /// <param name="shape">The sphere shape to cast.</param>
    public void CastSphere<TCollector>(ref TCollector collector, in ShapeSphere3D shape) where TCollector : ICollisionCollector
    {
        ObjectCollectorAdapter<TCollector> adapter = new ObjectCollectorAdapter<TCollector>(_targets, collector);
        _bvh.CastSphere(shape, ref adapter);
        collector = adapter.UserCollector;
    }

    /// <summary>
    /// Casts a box collider against the world and collects hits using the provided collector.
    /// </summary>
    /// <typeparam name="TCollector">The type of the collector.</typeparam>
    /// <param name="collector">The collector to gather hit results.</param>
    /// <param name="shape">The box shape to cast.</param>
    public void CastBox<TCollector>(ref TCollector collector, in ShapeBox3D shape) where TCollector : ICollisionCollector
    {
        ObjectCollectorAdapter<TCollector> adapter = new ObjectCollectorAdapter<TCollector>(_targets, collector);
        _bvh.CastBox(shape, ref adapter);
        collector = adapter.UserCollector;
    }

    /// <summary>
    /// Casts a point against the world and collects hits using the provided collector.
    /// </summary>
    /// <typeparam name="TCollector">The type of the collector.</typeparam>
    /// <param name="collector">The collector to gather hit results.</param>
    /// <param name="point">The point to cast.</param>
    public void CastPoint<TCollector>(ref TCollector collector, Vector3 point) where TCollector : ICollisionCollector
    {
        ObjectCollectorAdapter<TCollector> adapter = new ObjectCollectorAdapter<TCollector>(_targets, collector);
        _bvh.CastPoint(point, ref adapter);
        collector = adapter.UserCollector;
    }

    /// <summary>
    /// Casts a 3D oriented box shape and collects hit targets into a provided collection.
    /// </summary>
    public void CastBox<TTarget>(ICollection<TTarget> collector, in ShapeBox3D shape) where TTarget : class
    {
        var adapter = new CollectionCollector<TTarget>(collector);
        CastBox(ref adapter, shape);
    }

    /// <summary>
    /// Casts a 3D sphere shape and collects hit targets into a provided collection.
    /// </summary>
    public void CastSphere<TTarget>(ICollection<TTarget> collector, in ShapeSphere3D shape) where TTarget : class
    {
        var adapter = new CollectionCollector<TTarget>(collector);
        CastSphere(ref adapter, shape);
    }

    /// <summary>
    /// Casts a point and collects hit targets into a provided collection.
    /// </summary>
    public void CastPoint<TTarget>(ICollection<TTarget> collector, in Vector3 point) where TTarget : class
    {
        var adapter = new CollectionCollector<TTarget>(collector);
        CastPoint(ref adapter, point);
    }

    /// <summary>
    /// Casts a ray against targets and returns the closest hit.
    /// </summary>
    public bool TryCastRay<TTarget>(in Ray3D ray, [NotNullWhen(true)] out TTarget? hitTarget, out RaycastHit3D hit) where TTarget : class
    {
        hitTarget = null;
        hit = default;

        RayCastResult3D result = _bvh.CastRay(ray);
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

    private void PushTargetCore<T>(object target, T* collider) where T : unmanaged, ICollider3D
    {
        _targets.Add(target);
        int targetIndex = _targets.Count - 1;
        ColliderRef3D colliderRef = ColliderRef3D.Create(collider);
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

    private struct ObjectCollectorAdapter<TUserCollector> : IBvhCollisionCollector3D
        where TUserCollector : ICollisionCollector
    {
        private readonly List<object> _targets;
        public TUserCollector UserCollector;

        public ObjectCollectorAdapter(List<object> targets, TUserCollector userCollector)
        {
            _targets = targets;
            UserCollector = userCollector;
        }

        public bool OnHit(ColliderCastResult3D result)
        {
            int id = result.Collider.UserData;
            object target = _targets[id];
            return UserCollector.OnHit(target);
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
