namespace Vocore;

public class CollisionWorld3D : AutoDisposable
{
    private readonly ParallelScheduler _scheduler;
    private readonly NativeBvh3D _staticBvh;
    private readonly NativeBvh3D _dynamicBvh;

    public CollisionWorld3D()
    {
        _scheduler = new ParallelScheduler("collision_world_thread");
        _staticBvh = new NativeBvh3D(_scheduler);
        _dynamicBvh = new NativeBvh3D(_scheduler);
    }

    protected override void Dispose(bool disposing)
    {
        _staticBvh.Dispose();
        _dynamicBvh.Dispose();
        _scheduler.Dispose();
    }
}