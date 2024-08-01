

using static SlangSharp.Slang;

namespace SlangSharp;

public struct SlangCompileResult
{
    public string EntryPoint;
    public SlangStage Stage;
    public byte[] Spirv;
}

public class SlangCompiler : IDisposable
{
    private readonly SlangSession _session;
    public BaseSlangFileSystem? FileSystem { get; set; }


    public SlangCompiler(BaseSlangFileSystem? fileSystem, string sessionName = "unnamed_session")
    {
        _session = spCreateSession(sessionName);
        FileSystem = fileSystem;
    }

    public SlangCompiler(string sessionName = "unnamed_session")
    {
        _session = spCreateSession(sessionName);
    }

    public SlangCompileResult[] Compile(string path, string code, SlangCompileOption? option = null)
    {
        SlangCompileOption compileOption = option ?? SlangCompileOption.Default;
        SlangSourceLanguage sourceLanguage = compileOption.SourceLanguage ?? SlangSourceLanguage.SLANG_SOURCE_LANGUAGE_SLANG;

        SlangCompileRequest request = MakeRequest(compileOption);

        int translationUnitIndex = spAddTranslationUnit(request, sourceLanguage, path);
        spAddTranslationUnitSourceString(request, translationUnitIndex, path, code);

        SlangResult result = spCompile(request);

        if (result.IsError)
        {
            throw new Exception(request.GetDiagnosticString());
        }

        SlangReflection reflection = spGetReflection(request);

        var entryCount = spReflection_getEntryPointCount(reflection);

        SlangCompileResult[] results = new SlangCompileResult[entryCount];


        for (uint i = 0; i < entryCount; i++)
        {
            SlangReflectionEntryPoint entryPoint = spReflection_getEntryPointByIndex(reflection, i);
            SlangStage entryStage = spReflectionEntryPoint_getStage(entryPoint);

            results[i] = new SlangCompileResult
            {
                Spirv = request.GetBytesByEntryPointIndex((int)i),
                Stage = entryStage,
                EntryPoint = entryPoint.GetName()
            };
        }

        spDestroyCompileRequest(request);

        return results;
    }

    public void Dispose()
    {
        spDestroySession(_session);
    }

    private unsafe SlangCompileRequest MakeRequest(SlangCompileOption compileOption)
    {
        SlangCompileRequest request = spCreateCompileRequest(_session);


        int targetIndex;

        if (compileOption.CompileTarget.HasValue)
        {
            targetIndex = spAddCodeGenTarget(request, compileOption.CompileTarget.Value);
        }
        else
        {
            targetIndex = spAddCodeGenTarget(request, SlangCompileTarget.SLANG_SPIRV);
        }

        if (FileSystem != null)
        {
            spSetFileSystem(request, FileSystem.Handle);
        }

        if (compileOption.TargetFlags.HasValue)
        {
            spSetTargetFlags(request, targetIndex, compileOption.TargetFlags.Value);
        }

        if (compileOption.OptimizationLevel.HasValue)
        {
            spSetOptimizationLevel(request, compileOption.OptimizationLevel.Value);
        }

        if (compileOption.LineDirectiveMode.HasValue)
        {
            spSetLineDirectiveMode(request, compileOption.LineDirectiveMode.Value);
        }

        if (compileOption.MatrixLayoutMode.HasValue)
        {
            spSetMatrixLayoutMode(request, compileOption.MatrixLayoutMode.Value);
        }

        if (compileOption.PassThrough.HasValue)
        {
            spSetPassThrough(request, compileOption.PassThrough.Value);
        }

        if (compileOption.FloatingPointMode.HasValue)
        {
            spSetTargetFloatingPointMode(request, targetIndex, compileOption.FloatingPointMode.Value);
        }

        if (compileOption.DebugInfoLevel.HasValue)
        {
            spSetDebugInfoLevel(request, compileOption.DebugInfoLevel.Value);
        }

        if (compileOption.DebugInfoFormat.HasValue)
        {
            spSetDebugInfoFormat(request, compileOption.DebugInfoFormat.Value);
        }

        if (compileOption.DiagnosticFlags.HasValue)
        {
            spSetDiagnosticFlags(request, compileOption.DiagnosticFlags.Value);
        }

        if(compileOption.Macros != null)
        {
            for(int i = 0; i < compileOption.Macros.Length; i++)
            {
                spAddPreprocessorDefine(request, compileOption.Macros[i].name, compileOption.Macros[i].value);
            }
        }

        return request;
    }
}