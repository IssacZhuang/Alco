using System;
using System.Text;
using Silk.NET.OpenAL;

namespace Alco.Audio.OpenAL;

/// <summary>
/// Encapsulates OpenAL Soft context-level extensions:
/// ALC_EXT_disconnect (device connection status) and
/// ALC_SOFT_reopen_device (hot-plug recovery / device switching).
/// </summary>
internal unsafe class ALContextHelper
{
    private const int ALC_CONNECTED = 0x313;

    private static readonly ALContext ALC = ALContext.GetApi(true);

    private readonly delegate* unmanaged[Cdecl]<Device*, byte*, int*, int> _alcReopenDevice;

    /// <summary>
    /// Whether the ALC_SOFT_reopen_device extension is available.
    /// </summary>
    public bool IsReopenSupported => _alcReopenDevice != null;

    public ALContextHelper(Device* device)
    {
        nint reopenPtr = (nint)ALC.GetProcAddress(device, "alcReopenDeviceSOFT");
        if (reopenPtr != 0)
        {
            _alcReopenDevice = (delegate* unmanaged[Cdecl]<Device*, byte*, int*, int>)reopenPtr;
        }
    }

    /// <summary>
    /// Checks whether the underlying audio output device is still connected
    /// via the ALC_EXT_disconnect extension.
    /// </summary>
    public bool IsDeviceConnected(Device* device)
    {
        int connected = 0;
        ALC.GetContextProperty(device, (GetContextInteger)ALC_CONNECTED, 1, &connected);
        return connected != 0;
    }

    /// <summary>
    /// Attempts to reopen the device on a different output using ALC_SOFT_reopen_device.
    /// Pass null to reopen on the current default device.
    /// </summary>
    /// <param name="device">The device handle to reopen.</param>
    /// <param name="deviceName">Target device name, or null for default.</param>
    /// <returns>True if the device was successfully reopened.</returns>
    public bool TryReopenDevice(Device* device, string? deviceName)
    {
        if (_alcReopenDevice == null) return false;

        try
        {
            int result;
            if (deviceName == null)
            {
                result = _alcReopenDevice(device, null, null);
            }
            else
            {
                int byteCount = Encoding.UTF8.GetByteCount(deviceName) + 1;
                byte* pName = stackalloc byte[byteCount];
                Encoding.UTF8.GetBytes(deviceName, new Span<byte>(pName, byteCount - 1));
                pName[byteCount - 1] = 0;
                result = _alcReopenDevice(device, pName, null);
            }

            return result != 0;
        }
        catch
        {
            return false;
        }
    }
}
