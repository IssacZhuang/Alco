using System.Collections.Generic;

namespace Vocore;

public unsafe class CollisionWorld3D : AutoDisposable
{
    private readonly ParallelScheduler _scheduler;
    private readonly NativeBvh3D _Bvh;

    private readonly List<object> _targets;
    private readonly List<ICollisionCaster> _casters;

    private NativeArrayList<ColliderBox3D> _boxes;
    private NativeArrayList<ColliderSphere3D> _spheres;
    private NativeArrayList<ColliderRef3D> _colliders;

    

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
        _boxes.Add(new ColliderBox3D
        {
            shape = shape
        });

        ColliderBox3D* collider = _boxes.UnsafePointer + _boxes.Length - 1;

        AddTarget(target, collider);
    }

    /// <summary>
    /// Add object that waits for being hit. This object can only be hit but cannot hit other objects.
    /// </summary>
    /// <param name="target"> Object that waits for being hit. </param>
    /// <param name="shape"> Shape of the object. </param>    
    public void AddTarget(object target, ShapeSphere3D shape)
    {
        _spheres.Add(new ColliderSphere3D
        {
            shape = shape
        });

        ColliderSphere3D* collider = _spheres.UnsafePointer + _spheres.Length - 1;

        AddTarget(target, collider);
    }

    private void AddTarget<T>(object target, T* collider) where T : unmanaged, ICollider3D
    {
        _colliders.Add(ColliderRef3D.Create(collider));
        _targets.Add(target);
    }

    /// <summary>
    /// Add object that can hit other objects. This object can only hit other objects but cannot be hit.
    /// </summary>
    /// <param name="caster"> Object that can hit other objects. </param>
    /// <param name="shape"> Shape of the object. </param>
    public void AddCaster(ICollisionCaster caster, ShapeBox3D shape)
    {

    }

    /// <summary>
    /// Add object that can hit other objects. This object can only hit other objects but cannot be hit.
    /// </summary>
    /// <param name="caster"> Object that can hit other objects. </param>
    /// <param name="shape"> Shape of the object. </param>
    public void AddCaster(ICollisionCaster caster, ShapeSphere3D shape)
    {

    }

    protected override void Dispose(bool disposing)
    {
        _Bvh.Dispose();
        _scheduler.Dispose();
    }
}