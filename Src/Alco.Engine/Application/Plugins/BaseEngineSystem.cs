namespace Alco.Engine;

/// <summary>
/// Base implementation of <see cref="IEngineSystem"/> with empty virtual methods.
/// Override only the lifecycle hooks you need.
/// </summary>
public abstract class BaseEngineSystem : IEngineSystem
{
    public virtual int Order => 0;

    public virtual void OnStart() { }
    public virtual void OnTick(float delta) { }
    public virtual void OnPostTick(float delta) { }
    public virtual void OnUpdate(float delta) { }
    public virtual void OnPostUpdate(float delta) { }
    public virtual void OnBeginFrame(float deltaTime) { }
    public virtual void OnEndFrame(float deltaTime) { }
    public virtual void OnStop() { }

    public virtual void Dispose() { }
}
