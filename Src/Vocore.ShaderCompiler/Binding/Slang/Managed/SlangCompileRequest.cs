using System.Runtime.InteropServices;

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
        Slang.spSetCodeGenTarget(Handle, target);
    }

    public void SetMatrixLayoutMode(SlangMatrixLayoutMode mode)
    {
        Slang.spSetMatrixLayoutMode(Handle, mode);
    }

    public void SetTargetFlags(int targetIndex, SlangTargetFlags flags)
    {
        Slang.spSetTargetFlags(Handle, targetIndex, flags);
    }

    public void SetTargetLineDirectiveMode(int targetIndex, SlangLineDirectiveMode mode)
    {
        Slang.spSetTargetLineDirectiveMode(Handle, targetIndex, mode);
    }

    public int AddTranslationUnit(SlangSourceLanguage language, string name)
    {
        return Slang.spAddTranslationUnit(Handle, language, name);
    }

    public void AddTranslationUnitSourceFile(int translationUnitIndex, string path)
    {
        Slang.spAddTranslationUnitSourceFile(Handle, translationUnitIndex, path);
    }

    public void AddTranslationUnitSourceString(int translationUnitIndex, string path, string source)
    {
        Slang.spAddTranslationUnitSourceString(Handle, translationUnitIndex, path, source);
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
        Slang.spAddSearchPath(Handle, searchDir);
    }

    public void AddPreprocessorDefine(string key, string val)
    {
        Slang.spAddPreprocessorDefine(Handle, key, val);
    }

    public void ProcessCommandLineArguments(string[] args)
    {
        SlangResult result = Slang.spProcessCommandLineArguments(Handle, args, args.Length);
        if (result.IsError)
        {
            throw new SlangException($"Failed to process command line arguments. {result}");
        }
    }

    public string Compile()
    {
        SlangResult result = Slang.spCompile(Handle);
        if (result.IsError)
        {
            throw new SlangException($"Failed to compile. {result}");
        }

        IntPtr ptrStr = Slang.spGetCompileRequestCode(Handle, out nuint size);
        return UtilsSlangInterop.GetString(ptrStr, size);
    }

    public void Dispose()
    {
        Slang.spDestroyCompileRequest(Handle);
    }

    [UnmanagedCallersOnly]
    private static void SlangDiagnosticCallback(IntPtr message, IntPtr userData)
    {

    }
}

