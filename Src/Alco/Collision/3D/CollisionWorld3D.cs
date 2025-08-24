using System;
using System.Collections.Generic;
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
    private readonly List<ICollisionCaster> _colliderCasters = new List<ICollisionCaster>();

    private MiniHeap<ColliderBox3D> _targetBoxes;
    private MiniHeap<ColliderSphere3D> _targetSpheres;
    private NativeArrayList<ColliderRef3D> _targetColliders;

    private MiniHeap<ColliderBox3D> _casterBoxes;
    private MiniHeap<ColliderSphere3D> _casterSpheres;
    private NativeArrayList<ColliderRef3D> _colliderOfCaster;//same index as _colliderCasters

    private NativeArrayList<Ray3D> _rayOfCaster;//same index as _rayCasters
    private NativeArrayList<int> _rayUserDataOfCaster;//same index as _rayCasters
    private readonly List<IRayCaster3D> _rayCasters = new List<IRayCaster3D>();


    public CollisionWorld3D()
    {
        _bvh = new NativeBvh3D();
    }

    /// <summary>
    /// Add object that waits for being hit. This object can only be hit but cannot hit other objects.
    /// </summary>
    /// /// <param name="target"> Object that waits for being hit. </param>
    /// <param name="shape"> Shape of the object. </param>
    public void PushCollisionTarget(object target, ShapeBox3D shape)
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
    public void PushCollisionTarget(object target, ShapeSphere3D shape)
    {
        ColliderSphere3D* collider = _targetSpheres.Alloc(new ColliderSphere3D
        {
            shape = shape
        });

        PushTargetCore(target, collider);
    }



    /// <summary>
    /// Add object that can hit other objects. This object can only hit other objects but cannot be hit.
    /// </summary>
    /// <param name="caster"> Object that can hit other objects. </param>
    /// <param name="shape"> Shape of the object. </param>
    public void PushCollisionCaster(ICollisionCaster caster, ShapeBox3D shape, int userData = 0)
    {
        ColliderBox3D* collider = _casterBoxes.Alloc(new ColliderBox3D
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
    public void PushCollisionCaster(ICollisionCaster caster, ShapeSphere3D shape, int userData = 0)
    {
        ColliderSphere3D* collider = _casterSpheres.Alloc(new ColliderSphere3D
        {
            shape = shape
        });

        PushColliderCasterCore(caster, collider, userData);
    }

    /// <summary>
    /// Add ray caster that can raycast into the world. This caster only casts rays and cannot be hit.
    /// </summary>
    /// <param name="caster">Object that will receive hit callbacks.</param>
    /// <param name="ray">Ray to cast.</param>
    /// <param name="userData">Custom data passed back when a hit occurs.</param>
    public void PushRayCaster(IRayCaster3D caster, in Ray3D ray, int userData = 0)
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
            ReadOnlySpan<NativeArrayList<ColliderCastResult3D>> result = _bvh.CastBatchColliderRefCollector(_colliderOfCaster.AsSpan());
            for (int i = 0; i < _colliderCasters.Count; i++)
            {
                NativeArrayList<ColliderCastResult3D> hitTargets = result[i];
                ICollisionCaster caster = _colliderCasters[i];
                ColliderRef3D casterCollider = _colliderOfCaster[i];
                for (int j = 0; j < hitTargets.Length; j++)
                {
                    ColliderCastResult3D target = hitTargets[j];
                    int targetIndex = target.Collider.UserData;
                    caster.OnHit(_targets[targetIndex], casterCollider.UserData);
                }
            }
        }

        if (_rayCasters.Count != 0)
        {
            ReadOnlySpan<RayCastResult3D> result2 = _bvh.CastBatchRayFirstHit(_rayOfCaster.AsSpan());
            for (int i = 0; i < _rayCasters.Count; i++)
            {
                RayCastResult3D hit = result2[i];

                if (!hit.Hit)
                {
                    continue;
                }
                IRayCaster3D caster = _rayCasters[i];
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
    public void CastCollider(ICollisionCaster caster, ColliderRef3D collider, int userData = 0)
    {
        ReadOnlySpan<ColliderCastResult3D> result = _bvh.CastColliderRefCollector(collider);
        for (int i = 0; i < result.Length; i++)
        {
            ColliderCastResult3D target = result[i];
            caster.OnHit(_targets[target.Collider.UserData], userData);
        }
    }

    /// <summary>
    /// Cast a collider to hit the targets.
    /// </summary>
    /// <param name="caster">The caster that casts the collider. </param>
    /// <param name="collider">The collider that is casted. </param>
    /// <param name="userData">The custom data that is passed to the caster when the target is hit. </param>
    public void CastCollider<T>(ICollisionCaster caster, T collider, int userData = 0) where T : unmanaged, ICollider3D
    {
        ColliderRef3D colliderRef = ColliderRef3D.Create(&collider);
        CastCollider(caster, colliderRef, userData);
    }

    /// <summary>
    /// Cast a point to hit the targets.
    /// <br/> Not thread safe.
    /// </summary>
    /// <param name="caster">The caster that casts the point. </param>
    /// <param name="point">The point that is casted. </param>
    /// <param name="userData">The custom data that is passed to the caster when the target is hit. </param>
    public void CastPoint(ICollisionCaster caster, Vector3 point, int userData = 0)
    {
        ReadOnlySpan<ColliderCastResult3D> result = _bvh.CastPointRefCollector(point);
        for (int i = 0; i < result.Length; i++)
        {
            ColliderCastResult3D target = result[i];
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
        _colliderCasters.Clear();
        _casterBoxes.Reset();
        _casterSpheres.Reset();
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

    private void PushTargetCore<T>(object target, T* collider) where T : unmanaged, ICollider3D
    {
        _targets.Add(target);
        int targetIndex = _targets.Count - 1;
        ColliderRef3D colliderRef = ColliderRef3D.Create(collider);
        colliderRef.UserData = targetIndex;
        _targetColliders.Add(colliderRef);
    }


    private void PushColliderCasterCore<T>(ICollisionCaster caster, T* collider, int userData) where T : unmanaged, ICollider3D
    {
        ColliderRef3D colliderRef = ColliderRef3D.Create(collider);
        colliderRef.UserData = userData;
        _colliderOfCaster.Add(colliderRef);
        _colliderCasters.Add(caster);
    }

    private void PushRayCasterCore(IRayCaster3D caster, in Ray3D ray, int userData)
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