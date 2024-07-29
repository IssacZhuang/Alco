using System.Runtime.InteropServices;

using static SlangSharp.Slang;

namespace SlangSharp;

public readonly partial struct SlangCompileRequest
{
    public string GetDiagnosticString()
    {
        return UtilsSlangInterop.GetString(spGetDiagnosticOutput(Handle));
    }

    public string GetCode()
    {
        return UtilsSlangInterop.GetString(spGetCompileRequestCode(Handle, out nuint size), size);
    }

    public string GetCodeByEntryPointIndex(int entryPointIndex)
    {
        return UtilsSlangInterop.GetString(spGetEntryPointCode(Handle, entryPointIndex, out nuint size), size);
    }

    public byte[] GetBytes()
    {
        return UtilsSlangInterop.GetBytes(spGetCompileRequestCode(Handle, out nuint size), size);
    }

    public byte[] GetBytesByEntryPointIndex(int entryPointIndex)
    {
        return UtilsSlangInterop.GetBytes(spGetEntryPointCode(Handle, entryPointIndex, out nuint size), size);
    }

    public unsafe void AddSharedLibrary(string filename, byte[] lib)
    {
        fixed (byte* pLib = lib)
        {
            SlangResult result = spAddLibraryReference(Handle, filename, new nint(pLib), (nuint)lib.Length);
            if (result.IsError)
            {
                throw new Exception("Failed to add shared library reference.");
            }
        }
    }
}

public readonly partial struct SlangReflectionEntryPoint
{
    public string GetName()
    {
        if (this == Null)
        {
            throw new InvalidOperationException("The reflection entry point is null.");
        }

        return UtilsSlangInterop.GetString(Slang.spReflectionEntryPoint_getName(this));
    }
}