
using Microsoft.CodeAnalysis;

namespace Vocore.Tool.AssetsCompiler;

public abstract class BaseGenerator
{
    protected readonly GeneratorExecutionContext _context;

    public BaseGenerator(GeneratorExecutionContext context)
    {
        _context = context;
    }

    protected string GetMSBuildProperty(string name)
    {

        if (!_context.AnalyzerConfigOptions.GlobalOptions.TryGetValue($"build_property.{name}", out string? value))
        {
            Error($"MSBuild property '{name}' not found");
            return string.Empty;
        }

        return value;
    }


    protected void Log(string msg)
    {
        //log using diagnostic
        _context.ReportDiagnostic(
            Diagnostic.Create(
                new DiagnosticDescriptor("AssetsCompilerLog", "LOG", msg, "LOG", DiagnosticSeverity.Info, true), Location.None));
    }

    protected void Warning(string msg)
    {
        //log using diagnostic
        _context.ReportDiagnostic(
            Diagnostic.Create(
                new DiagnosticDescriptor("AssetsCompilerWarning", "WARNING", msg, "WARNING", DiagnosticSeverity.Warning, true), Location.None));
    }

    protected void Error(string msg)
    {
        //log using diagnostic
        _context.ReportDiagnostic(
            Diagnostic.Create(
                new DiagnosticDescriptor("AssetsCompilerError", "ERROR", msg, "ERROR", DiagnosticSeverity.Error, true), Location.None));
    }
}