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

    private MiniHeap<ColliderBox2D> _casterBoxes;
    private MiniHeap<ColliderSphere2D> _casterSpheres;
    private NativeArrayList<ColliderRef2D> _colliderOfCaster;//same index as _colliderCasters
    private readonly List<ICollisionCaster> _colliderCasters = new List<ICollisionCaster>();

    private NativeArrayList<Ray2D> _rayOfCaster;//same index as _rayCasters
    private NativeArrayList<int> _rayUserDataOfCaster;//same index as _rayCasters
    private readonly List<IRayCaster2D> _rayCasters = new List<IRayCaster2D>();


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
    /// Add object that can hit other objects. This object can only hit other objects but cannot be hit.
    /// </summary>
    /// <param name="caster"> Object that can hit other objects. </param>
    /// <param name="shape"> Shape of the object. </param>
    public void PushCollisionCaster(ICollisionCaster caster, in ShapeBox2D shape, int userData = 0)
    {
        ColliderBox2D* collider = _casterBoxes.Alloc(new ColliderBox2D
        {
            Shape = shape
        });

        PushColliderCasterCore(caster, collider, userData);
    }

    /// <summary>
    /// Add object that can hit other objects. This object can only hit other objects but cannot be hit.
    /// </summary>
    /// <param name="caster"> Object that can hit other objects. </param>
    /// <param name="shape"> Shape of the object. </param>
    public void PushCollisionCaster(ICollisionCaster caster, in ShapeSphere2D shape, int userData = 0)
    {
        ColliderSphere2D* collider = _casterSpheres.Alloc(new ColliderSphere2D
        {
            Shape = shape
        });

        PushColliderCasterCore(caster, collider, userData);
    }

    public void PushRayCaster(IRayCaster2D caster, in Ray2D ray, int userData = 0)
    {
        PushRayCasterCore(caster, ray, userData);
    }

    /// <summary>
    /// Build the BVH tree for the targets.
    /// </summary>
    public void BuildTree()
    {
        _bvh.BuildTree(_targetColliders.AsSpan());
    }

    /// <summary>
    /// Simulate the casters to hit the targets.
    /// <br/> This method is simulating the collision world in multi-threading.
    /// </summary>
    public void Simulate()
    {
        if (_colliderCasters.Count != 0)
        {
            ReadOnlySpan<NativeArrayList<ColliderCastResult2D>> result = _bvh.CastBatchColliderRefCollector(_colliderOfCaster.AsSpan());
            for (int i = 0; i < _colliderCasters.Count; i++)
            {
                NativeArrayList<ColliderCastResult2D> hitTargets = result[i];
                ICollisionCaster caster = _colliderCasters[i];
                ColliderRef2D casterCollider = _colliderOfCaster[i];
                for (int j = 0; j < hitTargets.Length; j++)
                {
                    ColliderCastResult2D target = hitTargets[j];
                    int targetIndex = target.Collider.UserData;
                    caster.OnHit(_targets[targetIndex], casterCollider.UserData);
                }
            }
        }

        if (_rayCasters.Count != 0)
        {
            ReadOnlySpan<RayCastResult2D> result2 = _bvh.CastBatchRayFirstHit(_rayOfCaster.AsSpan());
            for (int i = 0; i < _rayCasters.Count; i++)
            {
                RayCastResult2D hit = result2[i];

                if (!hit.Hit)
                {
                    continue;
                }
                IRayCaster2D caster = _rayCasters[i];
                int targetIndex = hit.Collider.UserData;
                caster.OnHit(_targets[targetIndex], hit.HitInfo, _rayUserDataOfCaster[i]);
            }
        }
    }

    /// <summary>
    /// Cast a collider to hit the targets.
    /// </summary>
    /// <param name="caster">The caster that casts the collider. </param>
    /// <param name="collider">The collider that is casted. </param>
    /// <param name="userData">The custom data that is passed to the caster when the target is hit. </param>
    public void CastCollider(ICollisionCaster caster, ColliderRef2D collider, int userData = 0)
    {
        ReadOnlySpan<ColliderCastResult2D> result = _bvh.CastColliderRefCollector(collider);
        for (int i = 0; i < result.Length; i++)
        {
            ColliderCastResult2D target = result[i];
            caster.OnHit(_targets[target.Collider.UserData], userData);
        }
    }

    /// <summary>
    /// Cast a collider to hit the targets.
    /// </summary>
    /// <param name="caster">The caster that casts the collider. </param>
    /// <param name="collider">The collider that is casted. </param>
    /// <param name="userData">The custom data that is passed to the caster when the target is hit. </param>
    public void CastCollider<T>(ICollisionCaster caster, T collider, int userData = 0) where T : unmanaged, ICollider2D
    {
        ColliderRef2D colliderRef = ColliderRef2D.Create(&collider);
        CastCollider(caster, colliderRef, userData);
    }

    /// <summary>
    /// Casts a 2D oriented box shape and collects hit targets directly into a provided set.
    /// </summary>
    /// <typeparam name="TTarget">The desired target object type to collect.</typeparam>
    /// <param name="collector">The destination set for collected targets.</param>
    /// <param name="shape">The box shape to cast.</param>
    public void CastBox<TTarget>(ISet<TTarget> collector, in ShapeBox2D shape) where TTarget : class
    {
        ColliderBox2D collider = new ColliderBox2D { Shape = shape };
        ColliderRef2D colliderRef = ColliderRef2D.Create(&collider);
        ReadOnlySpan<ColliderCastResult2D> result = _bvh.CastColliderRefCollector(colliderRef);
        for (int i = 0; i < result.Length; i++)
        {
            ColliderCastResult2D target = result[i];
            object obj = _targets[target.Collider.UserData];
            if (obj is TTarget t)
            {
                collector.Add(t);
            }
        }
    }

    /// <summary>
    /// Casts a 2D oriented box shape and collects hit targets into a provided collection.
    /// </summary>
    /// <typeparam name="TTarget">The desired target object type to collect.</typeparam>
    /// <param name="collector">The destination collection for collected targets.</param>
    /// <param name="shape">The box shape to cast.</param>
    public void CastBox<TTarget>(ICollection<TTarget> collector, in ShapeBox2D shape) where TTarget : class
    {
        ColliderBox2D collider = new ColliderBox2D { Shape = shape };
        ColliderRef2D colliderRef = ColliderRef2D.Create(&collider);
        ReadOnlySpan<ColliderCastResult2D> result = _bvh.CastColliderRefCollector(colliderRef);
        for (int i = 0; i < result.Length; i++)
        {
            ColliderCastResult2D target = result[i];
            object obj = _targets[target.Collider.UserData];
            if (obj is TTarget t)
            {
                collector.Add(t);
            }
        }
    }

    /// <summary>
    /// Casts a 2D sphere shape and collects hit targets directly into a provided set.
    /// </summary>
    /// <typeparam name="TTarget">The desired target object type to collect.</typeparam>
    /// <param name="collector">The destination set for collected targets.</param>
    /// <param name="shape">The sphere shape to cast.</param>
    public void CastSphere<TTarget>(ISet<TTarget> collector, in ShapeSphere2D shape) where TTarget : class
    {
        ColliderSphere2D collider = new ColliderSphere2D { Shape = shape };
        ColliderRef2D colliderRef = ColliderRef2D.Create(&collider);
        ReadOnlySpan<ColliderCastResult2D> result = _bvh.CastColliderRefCollector(colliderRef);
        for (int i = 0; i < result.Length; i++)
        {
            ColliderCastResult2D target = result[i];
            object obj = _targets[target.Collider.UserData];
            if (obj is TTarget t)
            {
                collector.Add(t);
            }
        }
    }

    /// <summary>
    /// Casts a 2D sphere shape and collects hit targets into a provided collection.
    /// </summary>
    /// <typeparam name="TTarget">The desired target object type to collect.</typeparam>
    /// <param name="collector">The destination collection for collected targets.</param>
    /// <param name="shape">The sphere shape to cast.</param>
    public void CastSphere<TTarget>(ICollection<TTarget> collector, in ShapeSphere2D shape) where TTarget : class
    {
        ColliderSphere2D collider = new ColliderSphere2D { Shape = shape };
        ColliderRef2D colliderRef = ColliderRef2D.Create(&collider);
        ReadOnlySpan<ColliderCastResult2D> result = _bvh.CastColliderRefCollector(colliderRef);
        for (int i = 0; i < result.Length; i++)
        {
            ColliderCastResult2D target = result[i];
            object obj = _targets[target.Collider.UserData];
            if (obj is TTarget t)
            {
                collector.Add(t);
            }
        }
    }

    /// <summary>
    /// Casts a point and collects hit targets directly into a provided set.
    /// </summary>
    /// <typeparam name="TTarget">The desired target object type to collect.</typeparam>
    /// <param name="collector">The destination set for collected targets.</param>
    /// <param name="point">The point to cast in world space.</param>
    public void CastPoint<TTarget>(ISet<TTarget> collector, in Vector2 point) where TTarget : class
    {
        ReadOnlySpan<ColliderCastResult2D> result = _bvh.CastPointRefCollector(point);
        for (int i = 0; i < result.Length; i++)
        {
            ColliderCastResult2D target = result[i];
            object obj = _targets[target.Collider.UserData];
            if (obj is TTarget t)
            {
                collector.Add(t);
            }
        }
    }

    /// <summary>
    /// Casts a point and collects hit targets into a provided collection.
    /// </summary>
    /// <typeparam name="TTarget">The desired target object type to collect.</typeparam>
    /// <param name="collector">The destination collection for collected targets.</param>
    /// <param name="point">The point to cast in world space.</param>
    public void CastPoint<TTarget>(ICollection<TTarget> collector, in Vector2 point) where TTarget : class
    {
        ReadOnlySpan<ColliderCastResult2D> result = _bvh.CastPointRefCollector(point);
        for (int i = 0; i < result.Length; i++)
        {
            ColliderCastResult2D target = result[i];
            object obj = _targets[target.Collider.UserData];
            if (obj is TTarget t)
            {
                collector.Add(t);
            }
        }
    }

    /// <summary>
    /// Cast a point to hit the targets.
    /// <br/> Not thread safe.
    /// </summary>
    /// <param name="caster">The caster that casts the point. </param>
    /// <param name="point">The point that is casted. </param>
    /// <param name="userData">The custom data that is passed to the caster when the target is hit. </param>
    public void CastPoint(ICollisionCaster caster, Vector2 point, int userData = 0)
    {
        ReadOnlySpan<ColliderCastResult2D> result = _bvh.CastPointRefCollector(point);
        for (int i = 0; i < result.Length; i++)
        {
            ColliderCastResult2D target = result[i];
            caster.OnHit(_targets[target.Collider.UserData], userData);
        }
    }


    public void ClearTargets()
    {
        _targets.Clear();
        _targetBoxes.Reset();
        _targetSpheres.Reset();
        _targetColliders.Clear();
    }

    public void ClearCasters()
    {

        _casterBoxes.Reset();
        _casterSpheres.Reset();

        _colliderCasters.Clear();
        _colliderOfCaster.Clear();

        _rayOfCaster.Clear();
        _rayUserDataOfCaster.Clear();
        _rayCasters.Clear();
    }

    public void ClearAll()
    {
        ClearTargets();
        ClearCasters();
    }

    private void PushTargetCore<T>(object target, T* collider) where T : unmanaged, ICollider2D
    {
        _targets.Add(target);
        int targetIndex = _targets.Count - 1;
        ColliderRef2D colliderRef = ColliderRef2D.Create(collider);
        colliderRef.UserData = targetIndex;
        _targetColliders.Add(colliderRef);
    }


    private void PushColliderCasterCore<T>(ICollisionCaster caster, T* collider, int userData) where T : unmanaged, ICollider2D
    {
        ColliderRef2D colliderRef = ColliderRef2D.Create(collider);
        colliderRef.UserData = userData;
        _colliderOfCaster.Add(colliderRef);
        _colliderCasters.Add(caster);
    }

    private void PushRayCasterCore(IRayCaster2D caster, in Ray2D ray, int userData)
    {
        _rayOfCaster.Add(ray);
        _rayUserDataOfCaster.Add(userData);
        _rayCasters.Add(caster);
    }

    protected override void Dispose(bool disposing)
    {
        _bvh.Dispose();

        _targetBoxes.Dispose();
        _targetSpheres.Dispose();
        _targetColliders.Dispose();

        _casterBoxes.Dispose();
        _casterSpheres.Dispose();
        _colliderOfCaster.Dispose();

        _rayOfCaster.Dispose();
        _rayUserDataOfCaster.Dispose();
    }
}