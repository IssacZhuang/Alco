namespace Vocore.Engine;

/// <summary>
/// The global component of the engine
/// </summary>
public interface IEngineSystem:IDisposable
{
    /// <summary>
    /// The execution order of the system. The lower the number, the earlier it will be executed.
    /// </summary>
    int Order { get; }
    /// <summary>
    /// Called when the engine starts
    /// </summary>
    void OnStart();
    /// <summary>
    /// Called every logic tick
    /// </summary>
    /// <param name="delta">The time since the last logic tick</param>
    void OnTick(float delta);
    /// <summary>
    /// Called after the logic tick
    /// </summary>
    /// <param name="delta">The time since the last logic tick</param>
    void OnPostTick(float delta);
    /// <summary>
    /// Called every frame
    /// </summary>
    /// <param name="delta">The time since the last frame</param>
    void OnUpdate(float delta);
    /// <summary>
    /// Called after the frame
    /// </summary>
    /// <param name="delta">The time since the last frame</param>
    void OnPostUpdate(float delta);
    /// <summary>
    /// Called before swapping the frame buffer
    /// </summary>
    void OnEndFrame();
    /// <summary>
    /// Called when the window is resized
    /// </summary>
    /// <param name="size">The new size of the window</param>
    void OnResize(int2 size);
    /// <summary>
    /// Called when the engine stops
    /// </summary>
    void OnStop();
}