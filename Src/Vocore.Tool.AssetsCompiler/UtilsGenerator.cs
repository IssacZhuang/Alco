using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Vocore.Tool.AssetsCompiler;

public static class UtilsGenerator
{
    public static void Log(this SourceProductionContext context, string msg)
    {
        //log using diagnostic
        context.ReportDiagnostic(
            Diagnostic.Create(
                new DiagnosticDescriptor("AssetsCompilerLog", "LOG", msg, "LOG", DiagnosticSeverity.Info, true), Location.None));
    }

    public static void Warning(this SourceProductionContext context, string msg)
    {
        //log using diagnostic
        context.ReportDiagnostic(
            Diagnostic.Create(
                new DiagnosticDescriptor("AssetsCompilerWarning", "WARNING", msg, "WARNING", DiagnosticSeverity.Warning, true), Location.None));
    }

    public static void Error(this SourceProductionContext context, string msg)
    {
        //log using diagnostic
        context.ReportDiagnostic(
            Diagnostic.Create(
                new DiagnosticDescriptor("AssetsCompilerError", "ERROR", msg, "ERROR", DiagnosticSeverity.Error, true), Location.None));
    }

    public static string GetMSBuildProperty(this AnalyzerConfigOptions options, string name)
    {
        if (!options.TryGetValue($"build_property.{name}", out string? value))
        {
            return string.Empty;
        }

        return value;
    }
}