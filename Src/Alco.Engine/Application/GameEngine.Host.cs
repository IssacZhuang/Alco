using Alco.Audio;
using Alco.IO;

namespace Alco.Engine;

/// <summary>
/// Host interface implementations that connect engine services.
/// </summary>
public partial class GameEngine :
IAssetSystemHost,
IAudioDeviceHost
{
    #region Host Interface
    private event Action? EventOnDispose;

    event Action IAssetSystemHost.OnDispose
    {
        add => EventOnDispose += value;
        remove => EventOnDispose -= value;
    }

    event Action IAudioDeviceHost.OnDispose
    {
        add => EventOnDispose += value;
        remove => EventOnDispose -= value;
    }

    void IAssetSystemHost.LogInfo(ReadOnlySpan<char> message)
    {
        Log.Info(message);
    }

    void IAssetSystemHost.LogWarning(ReadOnlySpan<char> message)
    {
        Log.Warning(message);
    }

    void IAssetSystemHost.LogError(ReadOnlySpan<char> message)
    {
        Log.Error(message);
    }

    void IAssetSystemHost.LogSuccess(ReadOnlySpan<char> message)
    {
        Log.Success(message);
    }

    void IAudioDeviceHost.LogInfo(ReadOnlySpan<char> message)
    {
        Log.Info(message);
    }

    void IAudioDeviceHost.LogWarning(ReadOnlySpan<char> message)
    {
        Log.Warning(message);
    }

    void IAudioDeviceHost.LogError(ReadOnlySpan<char> message)
    {
        Log.Error(message);
    }

    void IAudioDeviceHost.LogSuccess(ReadOnlySpan<char> message)
    {
        Log.Success(message);
    }

    #endregion
}
