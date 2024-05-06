using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Vocore.Unsafe;

namespace Vocore;

public unsafe class CollisionWorld2D : AutoDisposable
{
    private readonly ParallelScheduler _scheduler;
    private readonly bool _isSchedulerOwner;
    private readonly NativeBvh2D _bvh;

    public NativeBvh2D Bvh => _bvh;

    //the index of the target in the list is the index of the collider in the list
    private readonly List<object> _targets = new List<object>();
    private readonly List<ICollisionCaster> _casters = new List<ICollisionCaster>();

    private MiniHeap<ColliderBox2D> _targetBoxes;
    private MiniHeap<ColliderSphere2D> _targetSpheres;
    private NativeArrayList<ColliderRef2D> _targetColliders;

    private MiniHeap<ColliderBox2D> _casterBoxes;
    private MiniHeap<ColliderSphere2D> _casterSpheres;
    private NativeArrayList<ColliderRef2D> _casterColliders;



    public CollisionWorld2D()
    {
        _scheduler = new ParallelScheduler("collision_world_thread");
        _isSchedulerOwner = true;
        _bvh = new NativeBvh2D(_scheduler);
    }


    public CollisionWorld2D(int threadCount)
    {
        _scheduler = new ParallelScheduler(threadCount, "collision_world_thread");
        _isSchedulerOwner = true;
        _bvh = new NativeBvh2D(_scheduler);
    }

    public CollisionWorld2D(ParallelScheduler scheduler)
    {
        _scheduler = scheduler;
        _isSchedulerOwner = false;
        _bvh = new NativeBvh2D(_scheduler);
    }

    /// <summary>
    /// Add object that waits for being hit. This object can only be hit but cannot hit other objects.
    /// </summary>
    /// /// <param name="target"> Object that waits for being hit. </param>
    /// <param name="shape"> Shape of the object. </param>
    public void PushTarget(object target, ShapeBox2D shape)
    {
        ColliderBox2D* collider = _targetBoxes.Alloc(new ColliderBox2D
        {
            shape = shape
        });

        PushTargetCore(target, collider);
    }

    /// <summary>
    /// Add object that waits for being hit. This object can only be hit but cannot hit other objects.
    /// </summary>
    /// <param name="target"> Object that waits for being hit. </param>
    /// <param name="shape"> Shape of the object. </param>    
    public void PushTarget(object target, ShapeSphere2D shape)
    {
        ColliderSphere2D* collider = _targetSpheres.Alloc(new ColliderSphere2D
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
    public void PushCaster(ICollisionCaster caster, ShapeBox2D shape)
    {
        ColliderBox2D* collider = _casterBoxes.Alloc(new ColliderBox2D
        {
            shape = shape
        });

        PushCasterCore(caster, collider);
    }

    /// <summary>
    /// Add object that can hit other objects. This object can only hit other objects but cannot be hit.
    /// </summary>
    /// <param name="caster"> Object that can hit other objects. </param>
    /// <param name="shape"> Shape of the object. </param>
    public void PushCaster(ICollisionCaster caster, ShapeSphere2D shape)
    {
        ColliderSphere2D* collider = _casterSpheres.Alloc(new ColliderSphere2D
        {
            shape = shape
        });

        PushCasterCore(caster, collider);
    }

    public void BuildTree()
    {
        _bvh.BuildTree(_targetColliders.MemoryRef);
    }

    public void Simulate()
    {
        if (_casters.Count == 0)
        {
            return;
        }
        MemoryRef<NativeArrayList<ColliderCastResult2D>> result = _bvh.CastBatchColliderRefCollector(_casterColliders.MemoryRef);
        for (int i = 0; i < _casters.Count; i++)
        {
            NativeArrayList<ColliderCastResult2D> castResults = result[i];
            ICollisionCaster caster = _casters[i];
            for (int j = 0; j < castResults.Length; j++)
            {
                ColliderCastResult2D castResult = castResults[j];
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

    private void PushTargetCore<T>(object target, T* collider) where T : unmanaged, ICollider2D
    {
        _targets.Add(target);
        int targetIndex = _targets.Count - 1;
        ColliderRef2D colliderRef = ColliderRef2D.Create(collider);
        colliderRef.userData = targetIndex;
        _targetColliders.Add(colliderRef);
    }


    private void PushCasterCore<T>(ICollisionCaster caster, T* collider) where T : unmanaged, ICollider2D
    {
        _casterColliders.Add(ColliderRef2D.Create(collider));
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