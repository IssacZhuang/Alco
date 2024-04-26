namespace Vocore;

public class CollisionWorld2D : AutoDisposable
{
    private readonly ParallelScheduler _scheduler;
    private readonly NativeBvh2D _staticBvh;
    private readonly NativeBvh2D _dynamicBvh;

    public CollisionWorld2D()
    {
        _scheduler = new ParallelScheduler("collision_world_thread");
        _staticBvh = new NativeBvh2D(_scheduler);
        _dynamicBvh = new NativeBvh2D(_scheduler);
    }

    protected override void Dispose(bool disposing)
    {
        _staticBvh.Dispose();
        _dynamicBvh.Dispose();
        _scheduler.Dispose();
    }
}