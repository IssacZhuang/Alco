using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Vocore.Unsafe;

namespace Vocore;

public unsafe class CollisionWorld3D : AutoDisposable
{
    private readonly ParallelScheduler _scheduler;
    private readonly bool _isSchedulerOwner;
    private readonly NativeBvh3D _bvh;

    public NativeBvh3D Bvh => _bvh;

    //the index of the target in the list is the index of the collider in the list
    private readonly List<object> _targets = new List<object>();
    private readonly List<ICollisionCaster> _casters = new List<ICollisionCaster>();

    private MiniHeap<ColliderBox3D> _targetBoxes;
    private MiniHeap<ColliderSphere3D> _targetSpheres;
    private NativeArrayList<ColliderRef3D> _targetColliders;

    private MiniHeap<ColliderBox3D> _casterBoxes;
    private MiniHeap<ColliderSphere3D> _casterSpheres;
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
        ColliderBox3D* collider = _targetBoxes.Alloc(new ColliderBox3D
        {
            shape = shape
        });

        AddTarget(target, collider);
    }

    /// <summary>
    /// Add object that waits for being hit. This object can only be hit but cannot hit other objects.
    /// </summary>
    /// <param name="target"> Object that waits for being hit. </param>
    /// <param name="shape"> Shape of the object. </param>    
    public void AddTarget(object target, ShapeSphere3D shape)
    {
        ColliderSphere3D* collider = _targetSpheres.Alloc(new ColliderSphere3D
        {
            shape = shape
        });

        AddTarget(target, collider);
    }



    /// <summary>
    /// Add object that can hit other objects. This object can only hit other objects but cannot be hit.
    /// </summary>
    /// <param name="caster"> Object that can hit other objects. </param>
    /// <param name="shape"> Shape of the object. </param>
    public void AddCaster(ICollisionCaster caster, ShapeBox3D shape)
    {
        ColliderBox3D* collider = _casterBoxes.Alloc(new ColliderBox3D
        {
            shape = shape
        });

        AddCaster(caster, collider);
    }

    /// <summary>
    /// Add object that can hit other objects. This object can only hit other objects but cannot be hit.
    /// </summary>
    /// <param name="caster"> Object that can hit other objects. </param>
    /// <param name="shape"> Shape of the object. </param>
    public void AddCaster(ICollisionCaster caster, ShapeSphere3D shape)
    {
        ColliderSphere3D* collider = _casterSpheres.Alloc(new ColliderSphere3D
        {
            shape = shape
        });

        AddCaster(caster, collider);
    }

    public void BuildTree()
    {
        _bvh.BuildTree(_targetColliders.MemoryRef);
    }

    public void Simulate()
    {
        MemoryRef<NativeArrayList<ColliderCastResult3D>> result = _bvh.CastBatchColliderRefCollector(_casterColliders.MemoryRef);
        for (int i = 0; i < _casters.Count; i++)
        {
            NativeArrayList<ColliderCastResult3D> castResults = result[i];
            ICollisionCaster caster = _casters[i];
            for (int j = 0; j < castResults.Length; j++)
            {
                ColliderCastResult3D castResult = castResults[j];
                int targetIndex = castResult.collider.userData;
                caster.OnHit(_targets[targetIndex]);
            }
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
        _casters.Clear();
        _casterBoxes.Reset();
        _casterSpheres.Reset();
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