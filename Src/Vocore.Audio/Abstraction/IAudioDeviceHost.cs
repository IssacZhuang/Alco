namespace Vocore.Audio;

public interface IAudioDeviceHost
{
    event Action OnDispose;
    void LogInfo(ReadOnlySpan<char> message);
    void LogWarning(ReadOnlySpan<char> message);
    void LogError(ReadOnlySpan<char> message);
    void LogSuccess(ReadOnlySpan<char> message);
}