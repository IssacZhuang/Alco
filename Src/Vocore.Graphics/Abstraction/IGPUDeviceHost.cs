namespace Vocore.Graphics;

public interface IGPUDeviceHost
{
    event Action OnEndFrame;
    event Action OnDispose;
    void LogInfo(ReadOnlySpan<char> message);
    void LogWarning(ReadOnlySpan<char> message);
    void LogError(ReadOnlySpan<char> message);
    void LogSuccess(ReadOnlySpan<char> message);
}