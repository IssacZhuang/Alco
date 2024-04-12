namespace Vocore.Engine;

/// <summary>
/// The engine singleton interface
/// </summary>
public interface IEngineSystem
{
    /// <summary>
    /// The execution order of the system. The lower the number, the earlier it will be executed.
    /// </summary>
    int Order { get; }
    void OnStart();
    void OnTick(float delta);
    void OnUpdate(float delta);
    void OnResize(int2 size);
    void OnStop();
}