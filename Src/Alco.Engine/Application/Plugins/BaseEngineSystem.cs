namespace Alco.Engine;

/// <summary>
/// Base implementation of <see cref="IEngineSystem"/> with empty virtual methods.
/// Override only the lifecycle hooks you need.
/// </summary>
public abstract class BaseEngineSystem : IEngineSystem
{
    /// <summary>
    /// Gets the execution order; lower values run first.
    /// </summary>
    public virtual int Order => 0;

    /// <summary>
    /// Called once when the system starts.
    /// </summary>
    public virtual void OnStart() { }
    /// <summary>
    /// Called each fixed-rate tick.
    /// </summary>
    /// <param name="delta">Fixed time step in seconds.</param>
    public virtual void OnTick(float delta) { }
    /// <summary>
    /// Called each frame.
    /// </summary>
    /// <param name="delta">Elapsed time in seconds.</param>
    public virtual void OnUpdate(float delta) { }
    /// <summary>
    /// Called at the end of each frame, after scene rendering and before present.
    /// </summary>
    /// <param name="deltaTime">Elapsed time in seconds.</param>
    public virtual void OnEndFrame(float deltaTime) { }
    /// <summary>
    /// Called once after the main loop ends, before dispose.
    /// </summary>
    public virtual void OnStop() { }

    /// <summary>
    /// Releases resources held by the system.
    /// </summary>
    public virtual void Dispose() { }
}
