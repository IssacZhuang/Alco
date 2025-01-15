using Vocore.Audio;
using Vocore.Graphics;
using Vocore.IO;

namespace Vocore.Engine;

public partial class GameEngine :
IGPUDeviceHost,
IAssetSystemHost,
IAudioDeviceHost
{
    #region Host Interface
    private event Action? EventOnEndFrame;
    private event Action? EventOnHandleAssetLoaded;
    private event Action? EventOnDispose;

    event Action IGPUDeviceHost.OnEndFrame
    {
        add => EventOnEndFrame += value;
        remove => EventOnEndFrame -= value;
    }

    event Action IAssetSystemHost.OnHandleAssetLoaded
    {
        add => EventOnHandleAssetLoaded += value;
        remove => EventOnHandleAssetLoaded -= value;
    }

    event Action IAssetSystemHost.OnDispose
    {
        add => EventOnDispose += value;
        remove => EventOnDispose -= value;
    }

    event Action IGPUDeviceHost.OnDispose
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

    void IGPUDeviceHost.LogInfo(ReadOnlySpan<char> message)
    {
        Log.Info(message);
    }

    void IGPUDeviceHost.LogWarning(ReadOnlySpan<char> message)
    {
        Log.Warning(message);
    }

    void IGPUDeviceHost.LogError(ReadOnlySpan<char> message)
    {
        Log.Error(message);
    }

    void IGPUDeviceHost.LogSuccess(ReadOnlySpan<char> message)
    {
        Log.Success(message);
    }
    #endregion

}