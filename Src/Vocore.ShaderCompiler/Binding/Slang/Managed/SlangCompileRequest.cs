using System.Runtime.InteropServices;

using static SlangSharp.Slang;

namespace SlangSharp;

public class SlangCompileRequest : IDisposable
{


    public nint Handle { get; }

    public SlangCompileRequest(SlangSession session)
    {
        Handle = Slang.spCreateCompileRequest(session.Handle);
    }

    public void SetCodeGenTarget(SlangCompileTarget target)
    {
        spSetCodeGenTarget(Handle, target);
    }

    public void SetMatrixLayoutMode(SlangMatrixLayoutMode mode)
    {
        spSetMatrixLayoutMode(Handle, mode);
    }

    public void SetTargetFlags(int targetIndex, SlangTargetFlags flags)
    {
        spSetTargetFlags(Handle, targetIndex, flags);
    }

    public void SetTargetLineDirectiveMode(int targetIndex, SlangLineDirectiveMode mode)
    {
        spSetTargetLineDirectiveMode(Handle, targetIndex, mode);
    }

    public int AddTranslationUnit(SlangSourceLanguage language, string name)
    {
        return spAddTranslationUnit(Handle, language, name);
    }

    public void AddTranslationUnitSourceFile(int translationUnitIndex, string path)
    {
        spAddTranslationUnitSourceFile(Handle, translationUnitIndex, path);
    }

    public void AddTranslationUnitSourceString(int translationUnitIndex, string path, string source)
    {
        spAddTranslationUnitSourceString(Handle, translationUnitIndex, path, source);
    }

    public void AddEntryPoint(int translationUnitIndex, string name, SlangStage stage)
    {
        SlangResult result = Slang.spAddEntryPoint(Handle, translationUnitIndex, name, stage);
        if (result.IsError)
        {
            throw new SlangException($"Failed to add entry point: {name}. {result}");
        }
    }

    public void AddSearchPath(string searchDir)
    {
        spAddSearchPath(Handle, searchDir);
    }

    public void AddPreprocessorDefine(string key, string val)
    {
        spAddPreprocessorDefine(Handle, key, val);
    }

    public void ProcessCommandLineArguments(string[] args)
    {
        SlangResult result = spProcessCommandLineArguments(Handle, args, args.Length);
        if (result.IsError)
        {
            throw new SlangException($"Failed to process command line arguments. {result}");
        }
    }

    public byte[] Compile()
    {
        SlangResult result = spCompile(Handle);
        if (result.IsError)
        {
            throw new SlangException($"Failed to compile. {result}, \nMessgae:\n {GetDiagnosticOutput()}");
        }

        IntPtr ptrStr = spGetCompileRequestCode(Handle, out nuint size);
        return UtilsSlangInterop.GetData(ptrStr, size);
    }

    public void Dispose()
    {
        spDestroyCompileRequest(Handle); 
    }

    private string GetDiagnosticOutput()
    {
        IntPtr messgae = spGetDiagnosticOutput(Handle);
        return UtilsSlangInterop.GetString(messgae);
    }
}

