using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using SharpGen.Runtime;



namespace Vocore.Tool.AssetsCompiler;


public class BuiltInAssetsGenerator : BaseGenerator
{
    public const string PrefixShader = "Shader_";
    public const string PrefixFont = "Font_";

    public static readonly Regex VariableNameRegex = new Regex(@"^[a-zA-Z_][a-zA-Z0-9_]*$");

    public static readonly string AssetsPath = "Assets";

    public static readonly string GenFileName = "BuiltInAssets.gen.cs";

    public static readonly string GenFileContentBegin = @"
// Auto generated code
using System;
using Vocore.Rendering;

namespace Vocore.Engine;

public partial class BuiltInAssets
{
    ";

    public static readonly string GenFileContentEnd = @"
}
";

    public static readonly string GenStatementShader = @"    public Shader {0} => GetShader(""{1}"");";


    public static readonly string GenStatementFont = @"    public Font {0} => GetFont(""{1}"");";


    private readonly Dictionary<string, string> _duplicateCheck = new Dictionary<string, string>();
    public BuiltInAssetsGenerator(GeneratorExecutionContext context) : base(context)
    {

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
            if (ShouldGenerate(filePath, out string namePrefix, out string statement))
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
                code.AppendLine(string.Format(statement, variableName, localPath));
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

    protected static bool ShouldGenerate(string filePath, out string namePrefix, out string statement)
    {
        string extension = Path.GetExtension(filePath);
        switch (extension)
        {
            case ".ttf":
                statement = GenStatementFont;
                namePrefix = PrefixFont;
                return true;
            case ".hlsl":
                statement = GenStatementShader;
                namePrefix = PrefixShader;
                return true;
            default:
                statement = string.Empty;
                namePrefix = string.Empty;
                return false;
        }
    }



    private string FixWindowsPath(string path)
    {
        return path.Replace("\\", "/");
    }
}