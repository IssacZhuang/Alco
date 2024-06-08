using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;



namespace Vocore.Tool.AssetsCompiler;


public class BuiltInAssetLinkGenerator
{
    public const string PrefixShader = "Shader_";
    public const string PrefixFont = "Font_";

    public static readonly Regex VariableNameRegex = new Regex(@"^[a-zA-Z_][a-zA-Z0-9_]*$");

    public static readonly string GenFileName = "BuiltinAssetsPath.gen.cs";

    public static readonly string GenFileContentBegin = @"
// Auto generated code
using System;

namespace Vocore.Engine;

public static partial class BuiltInAssetsPath{
    ";

    public static readonly string GenFileContentEnd = @"
}
";

    private readonly GeneratorExecutionContext _context;
    public BuiltInAssetLinkGenerator(GeneratorExecutionContext context)
    {
        _context = context;
    }

    public void Execute()
    {
        string assetsPath = GetMSBuildProperty("AssetsPath");
        //enumerate all assets in local folder
        
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

    protected void Error(string msg)
    {
        //log using diagnostic
        _context.ReportDiagnostic(
            Diagnostic.Create(
                new DiagnosticDescriptor("AssetsCompilerError", "ERROR", msg, "ERROR", DiagnosticSeverity.Error, true), Location.None));
    }
}