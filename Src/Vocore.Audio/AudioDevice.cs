using System.Runtime.InteropServices;
using Silk.NET.OpenAL;
using Silk.NET.OpenAL.Extensions.Enumeration;

namespace Vocore.Audio;



public unsafe class AudioDevice : BaseAudioObject
{
    private static readonly Enumeration Enumeration;
    static AudioDevice()
    {
        if (Alc.TryGetExtension<Enumeration>(null, out var ext))
        {
            Enumeration = ext;
        }
        else
        {
            throw new NotSupportedException("Enumeration extension is not supported on this platform.");
        }
    }

    public static IEnumerable<string> GetDeviceNames()
    {
        return Enumeration.GetStringList(GetEnumerationContextStringList.DeviceSpecifiers);
    }

    public static string GetDefaultDeviceName()
    {
        return Enumeration.GetString(null, GetEnumerationContextString.DefaultDeviceSpecifier);
    }

    public AudioDevice()
    {

    }

    protected override void Dispose(bool disposing)
    {
        throw new NotImplementedException();
    }
}