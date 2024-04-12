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
    void OnStart();
    void OnTick(float delta);
    void OnPostTick(float delta);
    void OnUpdate(float delta);
    void OnPostUpdate(float delta);
    void OnResize(int2 size);
    void OnStop();
}