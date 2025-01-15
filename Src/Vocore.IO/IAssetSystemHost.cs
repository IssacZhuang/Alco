namespace Vocore.IO;

public interface IAssetSystemHost
{
    event Action OnHandleAssetLoaded;
    event Action OnDispose;
    void LogInfo(ReadOnlySpan<char> message);
    void LogWarning(ReadOnlySpan<char> message);
    void LogError(ReadOnlySpan<char> message);
    void LogSuccess(ReadOnlySpan<char> message);
}
