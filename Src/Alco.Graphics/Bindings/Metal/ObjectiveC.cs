// Minimal Objective-C runtime, class, selector, and objc_msgSend P/Invoke bindings for Metal.

using System.Runtime.InteropServices;

namespace Metal
{
    internal static unsafe partial class ObjectiveC
    {
        public const string ObjCRuntime = "/usr/lib/libobjc.A.dylib";
        public const string MetalFramework = "/System/Library/Frameworks/Metal.framework/Metal";

        [LibraryImport(ObjCRuntime, StringMarshalling = StringMarshalling.Utf8)]
        public static partial IntPtr objc_getClass(string name);

        [LibraryImport(ObjCRuntime, EntryPoint = "objc_msgSend")]
        public static partial void objc_msgSend(IntPtr receiver, Selector selector);

        [LibraryImport(ObjCRuntime, EntryPoint = "objc_msgSend")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static unsafe partial bool bool_objc_msgSend(IntPtr receiver, Selector selector, char* buffer, ulong maxBufferCount, ulong encoding);

        [LibraryImport(ObjCRuntime, EntryPoint = "objc_msgSend")]
        public static partial IntPtr IntPtr_objc_msgSend(IntPtr receiver, Selector selector);

        [LibraryImport(ObjCRuntime, EntryPoint = "objc_msgSend", StringMarshalling = StringMarshalling.Utf8)]
        public static partial IntPtr IntPtr_objc_msgSend(IntPtr receiver, Selector selector, string param);
    }

    internal struct ObjectiveCClass
    {
        public readonly IntPtr NativePtr;

        public static implicit operator IntPtr(ObjectiveCClass c) => c.NativePtr;

        public ObjectiveCClass(IntPtr ptr)
        {
            NativePtr = ptr;
        }

        public ObjectiveCClass(string name)
        {
            var ptr = ObjectiveC.objc_getClass(name);

            if (ptr == IntPtr.Zero)
            {
                Console.WriteLine($"Failed to get class {name}!");
            }

            NativePtr = ptr;
        }

        public ObjectiveCClass AllocInit()
        {
            var value = ObjectiveC.IntPtr_objc_msgSend(NativePtr, "alloc");
            ObjectiveC.objc_msgSend(value, "init");
            return new ObjectiveCClass(value);
        }
    }

    internal unsafe partial struct Selector
    {
        public readonly IntPtr SelPtr;

        [LibraryImport("/usr/lib/libobjc.A.dylib", StringMarshalling = StringMarshalling.Utf8)]
        private static unsafe partial IntPtr sel_getUid(string name);

        public Selector(string name)
        {
            var ptr = sel_getUid(name);

            if (ptr == IntPtr.Zero)
            {
                Console.WriteLine($"Failed to get selector {name}!");
            }

            SelPtr = ptr;
        }

        public static implicit operator Selector(string value) => new(value);

        public static implicit operator IntPtr(Selector sel) => sel.SelPtr;
    }

    internal static unsafe partial class ObjectiveCRuntime
    {
        [LibraryImport(ObjectiveC.ObjCRuntime, EntryPoint = "objc_msgSend")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool bool_objc_msgSend(IntPtr receiver, IntPtr selector);

        [LibraryImport(ObjectiveC.ObjCRuntime, EntryPoint = "objc_msgSend")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool bool_objc_msgSend(IntPtr receiver, IntPtr selector, long a);

        [LibraryImport(ObjectiveC.ObjCRuntime, EntryPoint = "objc_msgSend")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool bool_objc_msgSend(IntPtr receiver, IntPtr selector, IntPtr a, ref IntPtr b);

        [LibraryImport(ObjectiveC.ObjCRuntime, EntryPoint = "objc_msgSend")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool bool_objc_msgSend(IntPtr receiver, IntPtr selector, ulong a, ulong b);

        [LibraryImport(ObjectiveC.ObjCRuntime, EntryPoint = "objc_msgSend")]
        public static partial IntPtr IntPtr_objc_msgSend(IntPtr receiver, IntPtr selector);

        [LibraryImport(ObjectiveC.ObjCRuntime, EntryPoint = "objc_msgSend")]
        public static partial IntPtr IntPtr_objc_msgSend(IntPtr receiver, IntPtr selector, IntPtr a);

        [LibraryImport(ObjectiveC.ObjCRuntime, EntryPoint = "objc_msgSend")]
        public static partial IntPtr IntPtr_objc_msgSend(IntPtr receiver, IntPtr selector, NSRange a);

        [LibraryImport(ObjectiveC.ObjCRuntime, EntryPoint = "objc_msgSend")]
        public static partial IntPtr IntPtr_objc_msgSend(IntPtr receiver, IntPtr selector, ulong a);

        [LibraryImport(ObjectiveC.ObjCRuntime, EntryPoint = "objc_msgSend")]
        public static partial IntPtr IntPtr_objc_msgSend(IntPtr receiver, IntPtr selector, IntPtr a, ref IntPtr b);

        [LibraryImport(ObjectiveC.ObjCRuntime, EntryPoint = "objc_msgSend")]
        public static partial IntPtr IntPtr_objc_msgSend(IntPtr receiver, IntPtr selector, IntPtr a, ulong b);

        [LibraryImport(ObjectiveC.ObjCRuntime, EntryPoint = "objc_msgSend")]
        public static partial IntPtr IntPtr_objc_msgSend(IntPtr receiver, IntPtr selector, ulong a, ulong b);

        [LibraryImport(ObjectiveC.ObjCRuntime, EntryPoint = "objc_msgSend")]
        public static partial IntPtr IntPtr_objc_msgSend(IntPtr receiver, IntPtr selector, IntPtr a, IntPtr b, ref IntPtr c);

        [LibraryImport(ObjectiveC.ObjCRuntime, EntryPoint = "objc_msgSend")]
        public static partial IntPtr IntPtr_objc_msgSend(IntPtr receiver, IntPtr selector, IntPtr a, ulong b, ulong c);

        [LibraryImport(ObjectiveC.ObjCRuntime, EntryPoint = "objc_msgSend")]
        public static partial IntPtr IntPtr_objc_msgSend(IntPtr receiver, IntPtr selector, ulong a, ulong b, ulong c);

        [LibraryImport(ObjectiveC.ObjCRuntime, EntryPoint = "objc_msgSend")]
        public static partial long long_objc_msgSend(IntPtr receiver, IntPtr selector);

        [LibraryImport(ObjectiveC.ObjCRuntime, EntryPoint = "objc_msgSend")]
        public static partial MTLResourceID MTLResourceID_objc_msgSend(IntPtr receiver, IntPtr selector);

        [LibraryImport(ObjectiveC.ObjCRuntime, EntryPoint = "objc_msgSend")]
        public static partial MTLResourceID MTLResourceID_objc_msgSend(IntPtr receiver, IntPtr selector, IntPtr a, IntPtr b, ulong c);

        [LibraryImport(ObjectiveC.ObjCRuntime, EntryPoint = "objc_msgSend")]
        public static partial MTLResourceID MTLResourceID_objc_msgSend(IntPtr receiver, IntPtr selector, IntPtr a, NSRange b, ulong c);

        [LibraryImport(ObjectiveC.ObjCRuntime, EntryPoint = "objc_msgSend")]
        public static partial MTLSizeAndAlign MTLSizeAndAlign_objc_msgSend(IntPtr receiver, IntPtr selector, IntPtr a);

        [LibraryImport(ObjectiveC.ObjCRuntime, EntryPoint = "objc_msgSend")]
        public static partial MTLSizeAndAlign MTLSizeAndAlign_objc_msgSend(IntPtr receiver, IntPtr selector, ulong a, ulong b);

        [LibraryImport(ObjectiveC.ObjCRuntime, EntryPoint = "objc_msgSend")]
        public static partial ulong ulong_objc_msgSend(IntPtr receiver, IntPtr selector);

        [LibraryImport(ObjectiveC.ObjCRuntime, EntryPoint = "objc_msgSend")]
        public static partial ulong ulong_objc_msgSend(IntPtr receiver, IntPtr selector, ulong a);

        [LibraryImport(ObjectiveC.ObjCRuntime, EntryPoint = "objc_msgSend")]
        public static partial void objc_msgSend(IntPtr receiver, IntPtr selector);

        [LibraryImport(ObjectiveC.ObjCRuntime, EntryPoint = "objc_msgSend")]
        public static partial void objc_msgSend(IntPtr receiver, IntPtr selector, [MarshalAs(UnmanagedType.Bool)] bool a);

        [LibraryImport(ObjectiveC.ObjCRuntime, EntryPoint = "objc_msgSend")]
        public static partial void objc_msgSend(IntPtr receiver, IntPtr selector, double a);

        [LibraryImport(ObjectiveC.ObjCRuntime, EntryPoint = "objc_msgSend")]
        public static partial void objc_msgSend(IntPtr receiver, IntPtr selector, IntPtr a);

        [LibraryImport(ObjectiveC.ObjCRuntime, EntryPoint = "objc_msgSend")]
        public static partial void objc_msgSend(IntPtr receiver, IntPtr selector, long a);

        [LibraryImport(ObjectiveC.ObjCRuntime, EntryPoint = "objc_msgSend")]
        public static partial void objc_msgSend(IntPtr receiver, IntPtr selector, MTLClearColor a);

        [LibraryImport(ObjectiveC.ObjCRuntime, EntryPoint = "objc_msgSend")]
        public static partial void objc_msgSend(IntPtr receiver, IntPtr selector, MTLScissorRect a);

        [LibraryImport(ObjectiveC.ObjCRuntime, EntryPoint = "objc_msgSend")]
        public static partial void objc_msgSend(IntPtr receiver, IntPtr selector, MTLSize a);

        [LibraryImport(ObjectiveC.ObjCRuntime, EntryPoint = "objc_msgSend")]
        public static partial void objc_msgSend(IntPtr receiver, IntPtr selector, MTLViewport a);

        [LibraryImport(ObjectiveC.ObjCRuntime, EntryPoint = "objc_msgSend")]
        public static partial void objc_msgSend(IntPtr receiver, IntPtr selector, NSRange a);

        [LibraryImport(ObjectiveC.ObjCRuntime, EntryPoint = "objc_msgSend")]
        public static partial void objc_msgSend(IntPtr receiver, IntPtr selector, uint a);

        [LibraryImport(ObjectiveC.ObjCRuntime, EntryPoint = "objc_msgSend")]
        public static partial void objc_msgSend(IntPtr receiver, IntPtr selector, ulong a);

        [LibraryImport(ObjectiveC.ObjCRuntime, EntryPoint = "objc_msgSend")]
        public static partial void objc_msgSend(IntPtr receiver, IntPtr selector, IntPtr a, ulong b);

        [LibraryImport(ObjectiveC.ObjCRuntime, EntryPoint = "objc_msgSend")]
        public static unsafe partial void objc_msgSend(IntPtr receiver, IntPtr selector, MTL4CommandBuffer* a, ulong b);

        [LibraryImport(ObjectiveC.ObjCRuntime, EntryPoint = "objc_msgSend")]
        public static partial void objc_msgSend(IntPtr receiver, IntPtr selector, MTLSize a, MTLSize b);

        [LibraryImport(ObjectiveC.ObjCRuntime, EntryPoint = "objc_msgSend")]
        public static partial void objc_msgSend(IntPtr receiver, IntPtr selector, uint a, uint b);

        [LibraryImport(ObjectiveC.ObjCRuntime, EntryPoint = "objc_msgSend")]
        public static partial void objc_msgSend(IntPtr receiver, IntPtr selector, ulong a, MTLSize b);

        [LibraryImport(ObjectiveC.ObjCRuntime, EntryPoint = "objc_msgSend")]
        public static partial void objc_msgSend(IntPtr receiver, IntPtr selector, ulong a, ulong b);

        [LibraryImport(ObjectiveC.ObjCRuntime, EntryPoint = "objc_msgSend")]
        public static partial void objc_msgSend(IntPtr receiver, IntPtr selector, float a, float b, float c);

        [LibraryImport(ObjectiveC.ObjCRuntime, EntryPoint = "objc_msgSend")]
        public static partial void objc_msgSend(IntPtr receiver, IntPtr selector, MTLSize a, MTLSize b, MTLSize c);

        [LibraryImport(ObjectiveC.ObjCRuntime, EntryPoint = "objc_msgSend")]
        public static partial void objc_msgSend(IntPtr receiver, IntPtr selector, ulong a, MTLSize b, MTLSize c);

        [LibraryImport(ObjectiveC.ObjCRuntime, EntryPoint = "objc_msgSend")]
        public static partial void objc_msgSend(IntPtr receiver, IntPtr selector, ulong a, ulong b, ulong c);

        [LibraryImport(ObjectiveC.ObjCRuntime, EntryPoint = "objc_msgSend")]
        public static partial void objc_msgSend(IntPtr receiver, IntPtr selector, long a, ulong b, IntPtr c, ulong d);

        [LibraryImport(ObjectiveC.ObjCRuntime, EntryPoint = "objc_msgSend")]
        public static partial void objc_msgSend(IntPtr receiver, IntPtr selector, IntPtr a, ulong b, IntPtr c, ulong d, ulong e);

        [LibraryImport(ObjectiveC.ObjCRuntime, EntryPoint = "objc_msgSend")]
        public static partial void objc_msgSend(IntPtr receiver, IntPtr selector, ulong a, ulong b, ulong c, ulong d, ulong e);

        [LibraryImport(ObjectiveC.ObjCRuntime, EntryPoint = "objc_msgSend")]
        public static partial void objc_msgSend(IntPtr receiver, IntPtr selector, ulong a, ulong b, ulong c, ulong d, ulong e, ulong f, long g, ulong h);

        [LibraryImport(ObjectiveC.ObjCRuntime, EntryPoint = "objc_msgSend")]
        public static partial void objc_msgSend(IntPtr receiver, IntPtr selector, IntPtr a, ulong b, ulong c, MTLOrigin d, MTLSize e, IntPtr f, ulong g, ulong h, MTLOrigin i);

        [LibraryImport(ObjectiveC.ObjCRuntime, EntryPoint = "objc_msgSend")]
        public static partial void objc_msgSend(IntPtr receiver, IntPtr selector, IntPtr a, ulong b, ulong c, MTLOrigin d, MTLSize e, IntPtr f, ulong g, ulong h, ulong i);

        [LibraryImport(ObjectiveC.ObjCRuntime, EntryPoint = "objc_msgSend")]
        public static partial void objc_msgSend(IntPtr receiver, IntPtr selector, IntPtr a, ulong b, ulong c, ulong d, MTLSize e, IntPtr f, ulong g, ulong h, MTLOrigin i);
    }
}
