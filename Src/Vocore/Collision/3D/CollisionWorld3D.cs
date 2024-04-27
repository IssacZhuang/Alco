using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Vocore;

public unsafe class CollisionWorld3D : AutoDisposable
{
    private readonly ParallelScheduler _scheduler;
    private readonly NativeBvh3D _Bvh;

    //the index of the target in the list is the index of the collider in the list
    private readonly List<object> _targets;
    private readonly List<ICollisionCaster> _casters;

    private NativeArrayList<ColliderBox3D> _targetBoxes;
    private NativeArrayList<ColliderSphere3D> _targetSpheres;
    private NativeArrayList<ColliderRef3D> _targetColliders;

    private NativeArrayList<ColliderBox3D> _casterBoxes;
    private NativeArrayList<ColliderSphere3D> _casterSpheres;
    private NativeArrayList<ColliderRef3D> _casterColliders;

    

    public CollisionWorld3D()
    {
        _scheduler = new ParallelScheduler("collision_world_thread");
        _Bvh = new NativeBvh3D(_scheduler);
        _targets = new List<object>();
        _casters = new List<ICollisionCaster>();
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

    private void AddTarget<T>(object target, T* collider) where T : unmanaged, ICollider3D
    {
        _targetColliders.Add(ColliderRef3D.Create(collider));
        _targets.Add(target);
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


    private void AddCaster<T>(ICollisionCaster caster, T* collider) where T : unmanaged, ICollider3D
    {
        _casterColliders.Add(ColliderRef3D.Create(collider));
        _casters.Add(caster);
    }

    public void BuildTree()
    {
        
    }

    public void Simulate()
    {

    }

    protected override void Dispose(bool disposing)
    {
        _Bvh.Dispose();
        _scheduler.Dispose();

        _targetBoxes.Dispose();
        _targetSpheres.Dispose();
        _targetColliders.Dispose();

        _casterBoxes.Dispose();
        _casterSpheres.Dispose();
        _casterColliders.Dispose();
    }
}