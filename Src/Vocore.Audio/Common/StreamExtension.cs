namespace Vocore.Audio;

internal unsafe static class StreamExtension
{
    public static T Read<T>(this Stream stream) where T : unmanaged
    {
        T value;
        Span<byte> buffer = stackalloc byte[sizeof(T)];
        int read = stream.Read(buffer);
        if (read != sizeof(T))
        {
            throw new EndOfStreamException();
        }

        fixed (byte* ptr = buffer)
        {
            value = *(T*)ptr;
        }

        return value;
    }
}