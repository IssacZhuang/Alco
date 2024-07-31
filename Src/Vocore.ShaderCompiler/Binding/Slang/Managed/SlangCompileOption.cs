using static SlangSharp.Slang;

namespace SlangSharp;

public struct SlangCompileOption
{
    public static readonly SlangCompileOption Default = new SlangCompileOption{
        OptimizationLevel = SlangOptimizationLevel.SLANG_OPTIMIZATION_LEVEL_MAXIMAL,
        CompileTarget = SlangCompileTarget.SLANG_SPIRV,
        TargetFlags = SlangTargetFlags.SLANG_TARGET_FLAG_GENERATE_SPIRV_DIRECTLY,
        SourceLanguage = SlangSourceLanguage.SLANG_SOURCE_LANGUAGE_SLANG,
    };

    public SlangOptimizationLevel? OptimizationLevel;
    public SlangCompileTarget? CompileTarget;
    public SlangTargetFlags? TargetFlags;
    public SlangLineDirectiveMode? LineDirectiveMode;
    public SlangMatrixLayoutMode? MatrixLayoutMode;
    public SlangPassThrough? PassThrough;
    public SlangFloatingPointMode? FloatingPointMode;
    public SlangSourceLanguage? SourceLanguage;
    public SlangDebugInfoLevel? DebugInfoLevel;
    public SlangDebugInfoFormat? DebugInfoFormat;
    public SlangDiagnosticFlags? DiagnosticFlags;
}