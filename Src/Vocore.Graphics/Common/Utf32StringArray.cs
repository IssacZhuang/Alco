using static Vocore.Graphics.UtilsInterop;

namespace Vocore.Graphics;

internal unsafe struct Utf32StringArray: IDisposable
{
    // wchar_t** on unix, 4 bytes per wchar_t
    private readonly uint** _ptr;
    private readonly int _length;
    private bool _disposed;
    public IntPtr Pointer => (IntPtr)_ptr;
    public int Length => _length;

    public Utf32StringArray(string[] strings)
    {
        _disposed = false;
        _ptr = (uint**)Alloc<IntPtr>(strings.Length);
        _length = strings.Length;
        for (int i = 0; i < strings.Length; i++)
        {
            _ptr[i] = Alloc<uint>(strings[i].Length + 1);
            fixed (char* ptr = strings[i])
            {
                for (int j = 0; j < strings[i].Length; j++)
                {
                    _ptr[i][j] = ptr[j];
                }
                _ptr[i][strings[i].Length] = 0;
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        for (int i = 0; i < _length; i++)
        {
            Free(_ptr[i]);
        }
        Free(_ptr);
        _disposed = true;
    }
}
