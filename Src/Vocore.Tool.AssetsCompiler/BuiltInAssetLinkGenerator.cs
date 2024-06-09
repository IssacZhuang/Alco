using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using SharpGen.Runtime;



namespace Vocore.Tool.AssetsCompiler;


public class BuiltInAssetLinkGenerator
{
    public const string PrefixShader = "Shader_";
    public const string PrefixFont = "Font_";

    public static readonly Regex VariableNameRegex = new Regex(@"^[a-zA-Z_][a-zA-Z0-9_]*$");

    public static readonly string AssetsPath = "Assets";

    public static readonly string GenFileName = "BuiltinAssetsPath.gen.cs";

    public static readonly string GenFileContentBegin = @"
// Auto generated code
using System;

namespace Vocore.Engine;

public static partial class BuiltInAssetsPath
{
    ";

    public static readonly string GenFileContentEnd = @"
}
";

    public static readonly string GenStatementVariable = @"   public const string {0} = ""{1}"";";

    private readonly GeneratorExecutionContext _context;
    private readonly Dictionary<string, string> _duplicateCheck = new Dictionary<string, string>();
    public BuiltInAssetLinkGenerator(GeneratorExecutionContext context)
    {
        _context = context;
    }

    public void Execute()
    {
        string projectPath = GetMSBuildProperty("projectdir");
        string AssetsPath = Path.Combine(projectPath, "Assets");

        if (_context.AnalyzerConfigOptions.GlobalOptions.TryGetValue("build_property.BuiltInAssetsPath", out string? customAssetsPath))
        {
            AssetsPath = Path.Combine(projectPath, customAssetsPath);
        }

        StringBuilder code = new StringBuilder();

        code.AppendLine(GenFileContentBegin);
        //iterate the additional file and get local path
        foreach (AdditionalText file in _context.AdditionalFiles)
        {
            string filePath = file.Path;
            string localPath = Path.GetRelativePath(AssetsPath, filePath);
            localPath = FixWindowsPath(localPath);
            if (ShouldGenerate(filePath, out string namePrefix))
            {
                string fileName = Path.GetFileNameWithoutExtension(filePath);
                string variableName = namePrefix + fileName;

                if (!VariableNameRegex.IsMatch(fileName))
                {
                    Warning($"Invalid variable name '{fileName}', should match regex '{VariableNameRegex}' in '{filePath}'. Skipped");
                    continue;
                }

                if (_duplicateCheck.TryGetValue(variableName, out string? existingPath))
                {
                    Warning($"Duplicate variable name '{variableName}' found in '{existingPath}' and '{filePath}'. Skipped");
                    continue;
                }

                _duplicateCheck.Add(variableName, filePath);
                //Warning(string.Format(GenStatementVariable, variableName, localPath));
                code.AppendLine(string.Format(GenStatementVariable, variableName, localPath));
                code.AppendLine();
            }
        }

        code.AppendLine(GenFileContentEnd);

        _context.AddSource(GenFileName, code.ToString());

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

    protected bool ShouldGenerate(string filePath, out string namePrefix)
    {
        string extension = Path.GetExtension(filePath);
        switch (extension)
        {
            case ".ttf":
                namePrefix = PrefixFont;
                return true;
            case ".hlsl":
                namePrefix = PrefixShader;
                return true;
            default:
                namePrefix = string.Empty;
                return false;
        }
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

    private string FixWindowsPath(string path)
    {
        return path.Replace("\\", "/");
    }
}