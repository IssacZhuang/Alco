namespace Alco.IO;

public interface IAssetSystemHost
{
    event Action OnDispose;
    void PostToMainThread(Action action);
    void LogInfo(ReadOnlySpan<char> message);
    void LogWarning(ReadOnlySpan<char> message);
    void LogError(ReadOnlySpan<char> message);
    void LogSuccess(ReadOnlySpan<char> message);
}
