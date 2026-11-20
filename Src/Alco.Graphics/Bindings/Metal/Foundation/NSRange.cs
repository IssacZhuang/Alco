using System.Runtime.InteropServices;

namespace Metal
{
    [StructLayout(LayoutKind.Sequential)]
    public struct NSRange
    {
        public ulong location;
        public ulong length;
    }
}
