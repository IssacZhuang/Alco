using System.Runtime.InteropServices;

namespace Metal
{
    [StructLayout(LayoutKind.Sequential)]
    public struct NSOperatingSystemVersion
    {
        public long majorVersion;
        public long minorVersion;
        public long patchVersion;
    }
}
