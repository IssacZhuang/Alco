using Alco.Audio;
using Alco.Graphics;
using Alco.IO;
using Alco.Rendering;

namespace Alco.Engine;

public partial class GameEngine :
IGPUDeviceHost,
IAssetSystemHost,
IStorageSystemHost,
IRenderingSystemHost,
IAudioDeviceHost
{
    #region Host Interface
    private event Action? EventOnEndFrame;
    private event Action? EventOnHandleAssetLoaded;
    private event Action? EventOnDispose;
    private event Action<float>? EventOnUpdate;

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

    event Action<float> IRenderingSystemHost.OnUpdate
    {
        add => EventOnUpdate += value;
        remove => EventOnUpdate -= value;
    }

    event Action IRenderingSystemHost.OnDispose
    {
        add => EventOnDispose += value;
        remove => EventOnDispose -= value;
    }

    event Action IStorageSystemHost.OnDispose
    {
        add
        {
            EventOnDispose += value;
        }

        remove
        {
            EventOnDispose -= value;
        }
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

    void IStorageSystemHost.LogInfo(ReadOnlySpan<char> message)
    {
        Log.Info(message);
    }

    void IStorageSystemHost.LogWarning(ReadOnlySpan<char> message)
    {
        Log.Warning(message);
    }

    void IStorageSystemHost.LogError(ReadOnlySpan<char> message)
    {
        Log.Error(message);
    }

    void IStorageSystemHost.LogSuccess(ReadOnlySpan<char> message)
    {
        Log.Success(message);
    }
    #endregion

}