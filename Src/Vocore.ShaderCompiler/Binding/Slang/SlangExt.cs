using System.Runtime.InteropServices;

namespace SlangSharp;

public readonly partial struct SlangCompileRequest
{
    public string GetDiagnosticString()
    {
        return UtilsSlangInterop.GetString(Slang.spGetDiagnosticOutput(Handle));
    }

    public string GetCode()
    {
        return UtilsSlangInterop.GetString(Slang.spGetCompileRequestCode(Handle, out nuint size), size);
    }

    public byte[] GetBytes()
    {
        return UtilsSlangInterop.GetBytes(Slang.spGetCompileRequestCode(Handle, out nuint size), size);
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