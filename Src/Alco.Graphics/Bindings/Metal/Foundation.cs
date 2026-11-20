// Minimal Apple Foundation types required by Metal.

using System.Runtime.InteropServices;

namespace Metal
{
    internal enum NSStringEncoding : ulong
    {
        ASCII = 1,
        NEXTSTEP = 2,
        JapaneseEUC = 3,
        UTF8 = 4,
        ISOLatin1 = 5,
        Symbol = 6,
        NonLossyASCII = 7,
        ShiftJIS = 8,
        ISOLatin2 = 9,
        Unicode = 10,
        WindowsCP1251 = 11,
        WindowsCP1252 = 12,
        WindowsCP1253 = 13,
        WindowsCP1254 = 14,
        WindowsCP1250 = 15,
        ISO2022JP = 21,
        MacOSRoman = 30,
        UTF16 = Unicode,
        UTF16BigEndian = 0x90000100,
        UTF16LittleEndian = 0x94000100,
        UTF32 = 0x8c000100,
        UTF32BigEndian = 0x98000100,
        UTF32LittleEndian = 0x9c000100,
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NSRange
    {
        public ulong location;
        public ulong length;
    }

    internal struct NSObject : IDisposable
    {
        public IntPtr NativePtr;

        public static implicit operator IntPtr(NSObject obj) => obj.NativePtr;

        public NSObject(IntPtr ptr) => NativePtr = ptr;

        public void Dispose()
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_release);
        }

        private static readonly Selector sel_release = "release";
    }

    internal struct NSAutoreleasePool : IDisposable
    {
        public IntPtr NativePtr;

        public NSAutoreleasePool()
        {
            var cls = new ObjectiveCClass("NSAutoreleasePool");
            NativePtr = cls.AllocInit();
        }

        public void Dispose()
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_release);
        }

        private static readonly Selector sel_release = "release";
    }

    internal unsafe struct NSString : IDisposable
    {
        public IntPtr NativePtr;

        public static implicit operator IntPtr(NSString obj) => obj.NativePtr;

        public NSString(IntPtr ptr) => NativePtr = ptr;

        public void Dispose()
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_release);
        }

        public ulong Length => ObjectiveCRuntime.ulong_objc_msgSend(NativePtr, sel_length);

        public ulong MaximumLengthOfBytes(NSStringEncoding encoding)
        {
            return ObjectiveCRuntime.ulong_objc_msgSend(NativePtr, sel_maximumLengthOfBytesUsingEncoding, (ulong)encoding);
        }

        public static implicit operator NSString(string? value) => String(value);

        public static NSString String(string? value)
        {
            if (value == null)
            {
                return new NSString(IntPtr.Zero);
            }
            return new(ObjectiveC.IntPtr_objc_msgSend(new ObjectiveCClass("NSString"), sel_cStringWithUTF8String, value));
        }

        public override unsafe string? ToString()
        {
            if (NativePtr == IntPtr.Zero)
            {
                return null;
            }

            char[] sourceBuffer = new char[Length];
            fixed (char* pSourceBuffer = sourceBuffer)
            {
                ObjectiveC.bool_objc_msgSend(NativePtr, "getCString:maxLength:encoding:", pSourceBuffer, MaximumLengthOfBytes(NSStringEncoding.UTF16) + 1, (ulong)NSStringEncoding.UTF16);

                return new string(pSourceBuffer);
            }
        }

        private static readonly Selector sel_cStringWithUTF8String = "stringWithUTF8String:";
        private static readonly Selector sel_length = "length";
        private static readonly Selector sel_maximumLengthOfBytesUsingEncoding = "maximumLengthOfBytesUsingEncoding:";
        private static readonly Selector sel_release = "release";
    }

    internal struct NSURL : IDisposable
    {
        public IntPtr NativePtr;

        public static implicit operator IntPtr(NSURL obj) => obj.NativePtr;

        public NSURL(IntPtr ptr) => NativePtr = ptr;

        public void Dispose()
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_release);
        }

        public static NSURL FileURLWithPath(NSString pPath)
        {
            return new(ObjectiveCRuntime.IntPtr_objc_msgSend(new ObjectiveCClass("NSURL"), sel_fileURLWithPath, pPath));
        }

        private static readonly Selector sel_fileURLWithPath = "fileURLWithPath:";
        private static readonly Selector sel_release = "release";
    }

    internal struct NSData : IDisposable
    {
        public IntPtr NativePtr;

        public NSData(IntPtr ptr) => NativePtr = ptr;

        public void Dispose()
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_release);
        }

        public ulong Length => ObjectiveCRuntime.ulong_objc_msgSend(NativePtr, sel_length);

        private static readonly Selector sel_length = "length";
        private static readonly Selector sel_release = "release";
    }

    internal struct NSArray : IDisposable
    {
        public IntPtr NativePtr;

        public static implicit operator IntPtr(NSArray obj) => obj.NativePtr;

        public NSArray(IntPtr ptr) => NativePtr = ptr;

        public void Dispose()
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_release);
        }

        public ulong Count => ObjectiveCRuntime.ulong_objc_msgSend(NativePtr, sel_count);

        public static NSArray Array(NSObject pObject)
        {
            return new(ObjectiveCRuntime.IntPtr_objc_msgSend(new ObjectiveCClass("NSArray"), sel_arrayWithObject, pObject));
        }

        public IntPtr Object(ulong index)
        {
            return new(ObjectiveCRuntime.IntPtr_objc_msgSend(NativePtr, sel_objectAtIndex, index));
        }

        private static readonly Selector sel_arrayWithObject = "arrayWithObject:";
        private static readonly Selector sel_count = "count";
        private static readonly Selector sel_objectAtIndex = "objectAtIndex:";
        private static readonly Selector sel_release = "release";
    }

    internal struct NSError : IDisposable
    {
        public IntPtr NativePtr;

        public void Dispose()
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_release);
        }

        public long Code => ObjectiveCRuntime.long_objc_msgSend(NativePtr, sel_code);

        public NSString LocalizedDescription => new(ObjectiveCRuntime.IntPtr_objc_msgSend(NativePtr, sel_localizedDescription));

        private static readonly Selector sel_code = "code";
        private static readonly Selector sel_localizedDescription = "localizedDescription";
        private static readonly Selector sel_release = "release";
    }
}
