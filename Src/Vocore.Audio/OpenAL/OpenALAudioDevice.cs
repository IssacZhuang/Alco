using System.Runtime.InteropServices;
using Silk.NET.OpenAL;
using Silk.NET.OpenAL.Extensions.Enumeration;

namespace Vocore.Audio.OpenAL;



public unsafe class OpenALAudioDevice : AudioDevice
{
    private static readonly Enumeration Enumeration;
    protected static readonly AL Al = AL.GetApi();
    protected static readonly ALContext Alc = ALContext.GetApi();
    static OpenALAudioDevice()
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

    public OpenALAudioDevice()
    {

    }

    protected override AudioBuffer CreateBufferCore()
    {
        uint handle = Al.GenBuffer();
        return new OpenALAudioBuffer(handle);
    }

    protected override void Dispose(bool disposing)
    {

    }


}