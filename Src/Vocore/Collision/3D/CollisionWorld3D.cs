using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Vocore;

public unsafe class CollisionWorld3D : AutoDisposable
{
    private readonly ParallelScheduler _scheduler;
    private readonly bool _isSchedulerOwner;
    private readonly NativeBvh3D _bvh;

    public NativeBvh3D Bvh => _bvh;

    //the index of the target in the list is the index of the collider in the list
    private readonly List<object> _targets = new List<object>();
    private readonly List<object> _hitTargets = new List<object>();
    private readonly List<ICollisionCaster> _casters = new List<ICollisionCaster>();

    private NativeArrayList<ColliderBox3D> _targetBoxes;
    private NativeArrayList<ColliderSphere3D> _targetSpheres;
    private NativeArrayList<ColliderRef3D> _targetColliders;

    private NativeArrayList<ColliderBox3D> _casterBoxes;
    private NativeArrayList<ColliderSphere3D> _casterSpheres;
    private NativeArrayList<ColliderRef3D> _casterColliders;

    

    public CollisionWorld3D()
    {
        _scheduler = new ParallelScheduler("collision_world_thread");
        _isSchedulerOwner = true;
        _bvh = new NativeBvh3D(_scheduler);
    }


    public CollisionWorld3D(int threadCount)
    {
        _scheduler = new ParallelScheduler(threadCount, "collision_world_thread");
        _isSchedulerOwner = true;
        _bvh = new NativeBvh3D(_scheduler);
    }

    public CollisionWorld3D(ParallelScheduler scheduler)
    {
        _scheduler = scheduler;
        _isSchedulerOwner = false;
        _bvh = new NativeBvh3D(_scheduler);
    }

    /// <summary>
    /// Add object that waits for being hit. This object can only be hit but cannot hit other objects.
    /// </summary>
    /// /// <param name="target"> Object that waits for being hit. </param>
    /// <param name="shape"> Shape of the object. </param>
    public void AddTarget(object target, ShapeBox3D shape)
    {
        _targetBoxes.Add(new ColliderBox3D
        {
            shape = shape
        });

        ColliderBox3D* collider = _targetBoxes.UnsafePointer + _targetBoxes.Length - 1;

        AddTarget(target, collider);
    }

    /// <summary>
    /// Add object that waits for being hit. This object can only be hit but cannot hit other objects.
    /// </summary>
    /// <param name="target"> Object that waits for being hit. </param>
    /// <param name="shape"> Shape of the object. </param>    
    public void AddTarget(object target, ShapeSphere3D shape)
    {
        _targetSpheres.Add(new ColliderSphere3D
        {
            shape = shape
        });

        ColliderSphere3D* collider = _targetSpheres.UnsafePointer + _targetSpheres.Length - 1;

        AddTarget(target, collider);
    }



    /// <summary>
    /// Add object that can hit other objects. This object can only hit other objects but cannot be hit.
    /// </summary>
    /// <param name="caster"> Object that can hit other objects. </param>
    /// <param name="shape"> Shape of the object. </param>
    public void AddCaster(ICollisionCaster caster, ShapeBox3D shape)
    {
        _casterBoxes.Add(new ColliderBox3D
        {
            shape = shape
        });

        ColliderBox3D* collider = _casterBoxes.UnsafePointer + _casterBoxes.Length - 1;

        AddCaster(caster, collider);
    }

    /// <summary>
    /// Add object that can hit other objects. This object can only hit other objects but cannot be hit.
    /// </summary>
    /// <param name="caster"> Object that can hit other objects. </param>
    /// <param name="shape"> Shape of the object. </param>
    public void AddCaster(ICollisionCaster caster, ShapeSphere3D shape)
    {
        _casterSpheres.Add(new ColliderSphere3D
        {
            shape = shape
        });

        ColliderSphere3D* collider = _casterSpheres.UnsafePointer + _casterSpheres.Length - 1;

        AddCaster(caster, collider);
    }

    public void BuildTree()
    {
        _bvh.BuildTree(_targetColliders.MemoryRef);
    }

    public void Simulate()
    {
        // MemoryRef<NativeArrayList<ColliderCastResult3D>> result = _bvh.CastBatchColliderRefCollector(_casterColliders.MemoryRef);
        // for (int i = 0; i < _casters.Count; i++)
        // {
        //     NativeArrayList<ColliderCastResult3D> castResults = result[i];
        //     _hitTargets.Clear();
        //     for (int j = 0; j < castResults.Length; j++)
        //     {
        //         ColliderCastResult3D castResult = castResults[j];
        //         int targetIndex = castResult.collider.userData;
        //         _hitTargets.Add(_targets[targetIndex]);
        //     }

        //     ICollisionCaster caster = _casters[i];
        //     caster.OnHit(_hitTargets);
        // }

        // MemoryRef<ColliderCastResult3D> result = _bvh.CastBatchColliderRef(_casterColliders.MemoryRef);
        // for (int i = 0; i < _casters.Count; i++)
        // {
        //     ColliderCastResult3D castResult = result[i];
        //     if (castResult.hit)
        //     {
        //         ICollisionCaster caster = _casters[i];
        //         caster.OnHit([_targets[castResult.collider.userData]]);
        //     }
        // }

        for (int i = 0; i < _casters.Count; i++)
        {
            ICollisionCaster caster = _casters[i];
            ColliderCastResult3D result =  _bvh.CastCollider(ref _casterColliders.UnsafePointer[i]);
            if (result.hit)
            {
                int targetIndex = result.collider.userData;
                caster.OnHit(new object[] { _targets[targetIndex] });
            }
        }
    }


    public void ClearTargets()
    {
        _targets.Clear();
        _targetBoxes.Clear();
        _targetSpheres.Clear();
        _targetColliders.Clear();
    }

    public void ClearCasters()
    {
        _casters.Clear();
        _casterBoxes.Clear();
        _casterSpheres.Clear();
        _casterColliders.Clear();
    }

    public void ClearAll()
    {
        ClearTargets();
        ClearCasters();
    }

    private void AddTarget<T>(object target, T* collider) where T : unmanaged, ICollider3D
    {
        _targets.Add(target);
        int targetIndex = _targets.Count - 1;
        ColliderRef3D colliderRef = ColliderRef3D.Create(collider);
        colliderRef.userData = targetIndex;
        _targetColliders.Add(colliderRef);
    }


    private void AddCaster<T>(ICollisionCaster caster, T* collider) where T : unmanaged, ICollider3D
    {
        _casterColliders.Add(ColliderRef3D.Create(collider));
        _casters.Add(caster);
    }

    protected override void Dispose(bool disposing)
    {
        if (_isSchedulerOwner)
        {
            _scheduler.Dispose();
        }

        _bvh.Dispose();

        _targetBoxes.Dispose();
        _targetSpheres.Dispose();
        _targetColliders.Dispose();

        _casterBoxes.Dispose();
        _casterSpheres.Dispose();
        _casterColliders.Dispose();
    }
}