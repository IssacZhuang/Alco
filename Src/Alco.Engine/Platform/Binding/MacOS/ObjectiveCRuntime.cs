using System.Runtime.InteropServices;
using System.Text;

using static Alco.Engine.MacOS.ObjectiveCRuntime;

namespace Alco.Engine.MacOS;

/// <summary>P/Invoke bindings to the Objective-C runtime.</summary>
public static unsafe class ObjectiveCRuntime
{
    private const string ObjCLibrary = "/usr/lib/libobjc.A.dylib";

    /// <summary>Registers a selector name and returns its selector handle.</summary>
    /// <param name="namePtr">The null-terminated selector name.</param>
    /// <returns>The registered selector.</returns>
    [DllImport(ObjCLibrary)]
    public static extern IntPtr sel_registerName(byte* namePtr);

    /// <summary>Returns the name of a selector.</summary>
    /// <param name="selector">The selector handle.</param>
    /// <returns>The null-terminated selector name.</returns>
    [DllImport(ObjCLibrary)]
    public static extern byte* sel_getName(IntPtr selector);
    /// <summary>Returns the Objective-C class with the given name.</summary>
    /// <param name="namePtr">The null-terminated class name.</param>
    /// <returns>The class handle, or zero when the class is not found.</returns>
    [DllImport(ObjCLibrary)]
    public static extern IntPtr objc_getClass(byte* namePtr);

    /// <summary>Sends a message without arguments.</summary>
    /// <param name="receiver">The message receiver.</param>
    /// <param name="selector">The message selector.</param>
    [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
    public static extern void objc_msgSend(IntPtr receiver, Selector selector);
    /// <summary>Sends a message with a boolean argument.</summary>
    /// <param name="receiver">The message receiver.</param>
    /// <param name="selector">The message selector.</param>
    /// <param name="b">The boolean argument.</param>
    [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
    public static extern void objc_msgSend(IntPtr receiver, Selector selector, Bool8 b);
    /// <summary>Sends a message with a pointer argument.</summary>
    /// <param name="receiver">The message receiver.</param>
    /// <param name="selector">The message selector.</param>
    /// <param name="b">The pointer argument.</param>
    [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
    public static extern void objc_msgSend(IntPtr receiver, Selector selector, IntPtr b);

    /// <summary>Sends a message returning a boolean value.</summary>
    /// <param name="receiver">The message receiver.</param>
    /// <param name="selector">The message selector.</param>
    /// <returns>The returned boolean value.</returns>
    [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
    public static extern Bool8 bool8_objc_msgSend(IntPtr receiver, Selector selector);

    /// <summary>Sends a message returning a pointer-sized value.</summary>
    /// <param name="receiver">The message receiver.</param>
    /// <param name="selector">The message selector.</param>
    /// <returns>The returned pointer.</returns>
    [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
    public static extern IntPtr IntPtr_objc_msgSend(IntPtr receiver, Selector selector);

    /// <summary>Sends a message returning a reference to an unmanaged value.</summary>
    /// <param name="receiver">The message receiver.</param>
    /// <param name="selector">The message selector.</param>
    /// <returns>A reference to the returned value.</returns>
    public static ref T objc_msgSend<T>(IntPtr receiver, Selector selector) where T : unmanaged
    {
        IntPtr value = IntPtr_objc_msgSend(receiver, selector);
        T* ptr = (T*)value;
        return ref *ptr;
    }

    /// <summary>Reads a null-terminated UTF-8 string.</summary>
    /// <param name="stringStart">The start of the string.</param>
    /// <returns>The decoded string.</returns>
    public static unsafe string GetUtf8String(byte* stringStart)
    {
        int characters = 0;
        while (stringStart[characters] != 0)
        {
            characters++;
        }

        return Encoding.UTF8.GetString(stringStart, characters);
    }
}


/// <summary>An Objective-C selector handle.</summary>
public unsafe readonly struct Selector
{
    /// <summary>The native selector handle.</summary>
    public readonly IntPtr NativePtr;

    /// <summary>Wraps an existing selector handle.</summary>
    /// <param name="ptr">The selector handle.</param>
    public Selector(IntPtr ptr)
    {
        NativePtr = ptr;
    }

    /// <summary>Creates a selector from its name.</summary>
    /// <param name="name">The selector name.</param>
    public Selector(string name)
    {
        int byteCount = Encoding.UTF8.GetMaxByteCount(name.Length);
        byte* utf8BytesPtr = stackalloc byte[byteCount];
        fixed (char* namePtr = name)
        {
            Encoding.UTF8.GetBytes(namePtr, name.Length, utf8BytesPtr, byteCount);
        }

        NativePtr = sel_registerName(utf8BytesPtr);
    }

    /// <summary>Gets the selector name.</summary>
    public string Name
    {
        get
        {
            byte* name = sel_getName(NativePtr);
            return GetUtf8String(name);
        }
    }

    /// <summary>Converts a string to a selector.</summary>
    /// <param name="s">The selector name.</param>
    /// <returns>The selector.</returns>
    public static implicit operator Selector(string s) => new(s);
}

/// <summary>Selector names used by the Objective-C runtime.</summary>
public static class Selectors
{
    internal static readonly Selector alloc = "alloc";
    internal static readonly Selector init = "init";
}

/// <summary>An Objective-C class handle.</summary>
public unsafe readonly struct ObjCClass
{
    /// <summary>The native class handle.</summary>
    public readonly IntPtr NativePtr;
    /// <summary>Converts a class to its native handle.</summary>
    /// <param name="c">The class.</param>
    /// <returns>The native handle.</returns>
    public static implicit operator IntPtr(ObjCClass c) => c.NativePtr;

    /// <summary>Looks up an Objective-C class by name.</summary>
    /// <param name="name">The class name.</param>
    public ObjCClass(string name)
    {
        int byteCount = Encoding.UTF8.GetMaxByteCount(name.Length);
        byte* utf8BytesPtr = stackalloc byte[byteCount];
        fixed (char* namePtr = name)
        {
            Encoding.UTF8.GetBytes(namePtr, name.Length, utf8BytesPtr, byteCount);
        }

        NativePtr = objc_getClass(utf8BytesPtr);
    }

    /// <summary>Allocates and initializes an unmanaged instance of the class.</summary>
    /// <typeparam name="T">The unmanaged type of the instance handle.</typeparam>
    /// <returns>The allocated and initialized instance.</returns>
    public T AllocInit<T>() where T : unmanaged
    {
        IntPtr value = IntPtr_objc_msgSend(NativePtr, Selectors.alloc);
        objc_msgSend(value, Selectors.init);
        T* ptr = (T*)value;
        return *ptr;
    }
}

/// <summary>An Objective-C boolean value stored as a byte.</summary>
public readonly struct Bool8
{
    /// <summary>The stored byte value.</summary>
    public readonly byte Value;

    /// <summary>Creates a boolean from a byte value.</summary>
    /// <param name="value">The raw byte value.</param>
    public Bool8(byte value)
    {
        Value = value;
    }

    /// <summary>Creates a boolean from a managed boolean.</summary>
    /// <param name="value">The managed boolean.</param>
    public Bool8(bool value)
    {
        Value = value ? (byte)1 : (byte)0;
    }

    /// <summary>Converts to a managed boolean.</summary>
    /// <param name="b">The Objective-C boolean.</param>
    /// <returns>True when the stored value is non-zero.</returns>
    public static implicit operator bool(Bool8 b) => b.Value != 0;
    /// <summary>Converts to the stored byte value.</summary>
    /// <param name="b">The Objective-C boolean.</param>
    /// <returns>The stored byte.</returns>
    public static implicit operator byte(Bool8 b) => b.Value;
    /// <summary>Converts a managed boolean to an Objective-C boolean.</summary>
    /// <param name="b">The managed boolean.</param>
    /// <returns>The Objective-C boolean.</returns>
    public static implicit operator Bool8(bool b) => new(b);
}