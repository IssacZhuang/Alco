using static SlangSharp.Slang;

namespace SlangSharp;

public struct SlangCompileOption
{
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