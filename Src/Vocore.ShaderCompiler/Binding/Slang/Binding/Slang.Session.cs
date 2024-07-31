using System.Runtime.InteropServices;

namespace SlangSharp;

public static partial class Slang
{

    // SLANG_API SlangSession* spCreateSession(const char* deprecated = 0);
    [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern SlangSession spCreateSession([MarshalAs(UnmanagedType.LPStr)] string lpString);


    // SLANG_API void spDestroySession(
    //     SlangSession* session);
    [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void spDestroySession(SlangSession session);

    // SLANG_API void spSessionSetSharedLibraryLoader(
    //     SlangSession* session,
    //     ISlangSharedLibraryLoader* loader);
    // Currently not implemented

    // SLANG_API ISlangSharedLibraryLoader* spSessionGetSharedLibraryLoader(
    //     SlangSession* session);
    // Currently not implemented

    // SLANG_API SlangResult spSessionCheckCompileTargetSupport(
    //     SlangSession* session,
    //     SlangCompileTarget target);
    [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern SlangResult spSessionCheckCompileTargetSupport(SlangSession session, SlangCompileTarget target);

    // SLANG_API SlangResult spSessionCheckPassThroughSupport(
    //     SlangSession*       session,
    //     SlangPassThrough    passThrough
    // );
    [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern SlangResult spSessionCheckPassThroughSupport(SlangSession session, SlangPassThrough passThrough);

    // SLANG_API void spAddBuiltins(
    //     SlangSession* session,
    //     char const* sourcePath,
    //     char const* sourceString);
    [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void spAddBuiltins(SlangSession session, [MarshalAs(UnmanagedType.LPStr)] string sourcePath, [MarshalAs(UnmanagedType.LPStr)] string sourceString);

    // SLANG_API SlangProfileID spFindProfile(
    //     SlangSession* session,
    //     char const* name);
    [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern SlangProfileID spFindProfile(SlangSession session, [MarshalAs(UnmanagedType.LPStr)] string name);

    // SLANG_API SlangCapabilityID spFindCapability(
    //     SlangSession* session,
    //     char const* name);
    [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern SlangCapabilityID spFindCapability(SlangSession session, [MarshalAs(UnmanagedType.LPStr)] string name);

    // SLANG_API SlangResult spExtractRepro(
    //     SlangSession* session,
    //     const void* reproData,
    //     size_t reproDataSize,
    //     ISlangMutableFileSystem* fileSystem);
    // Currently not implemented

    // SLANG_API SlangResult spLoadReproAsFileSystem(
    //     SlangSession* session,
    //     const void* reproData,
    //     size_t reproDataSize,
    //     ISlangFileSystem* replaceFileSystem,
    //     ISlangFileSystemExt** outFileSystem);
    // Currently not implemented
}